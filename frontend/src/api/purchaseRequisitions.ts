import { z } from 'zod';
import type { ApiRequest } from './client';

export const PR_STATUSES = ['Draft', 'InReview', 'Rejected', 'Approved', 'Cancelled', 'Closed'] as const;
export const prStatusLabel = (s: string | null | undefined) => (s === 'InReview' ? 'In review' : (s ?? '—'));

const date = z.string().nullable();

export const prListItemSchema = z.object({
  requisitionNumber: z.string(),
  name: z.string().nullable(),
  status: z.string().nullable(),
  purpose: z.string().nullable(),
  preparer: z.string().nullable(),
  requestedDate: date,
});
export type PrListItem = z.infer<typeof prListItemSchema>;

export const prListSchema = z.object({ items: z.array(prListItemSchema), total: z.number().nullable() });
export type PrList = z.infer<typeof prListSchema>;

export const prLineSchema = z.object({
  lineNumber: z.number(),
  buyingLegalEntity: z.string().nullable(),
  itemNumber: z.string().nullable(),
  procurementCategory: z.string().nullable(),
  productName: z.string().nullable(),
  description: z.string().nullable(),
  quantity: z.number(),
  unit: z.string().nullable(),
  unitPrice: z.number(),
  lineAmount: z.number(),
  currency: z.string().nullable(),
  vendor: z.string().nullable(),
  site: z.string().nullable(),
  warehouse: z.string().nullable(),
  requestedDate: date,
  status: z.string().nullable(),
});
export type PrLine = z.infer<typeof prLineSchema>;

export const prDetailSchema = z.object({
  requisitionNumber: z.string(),
  name: z.string().nullable(),
  status: z.string().nullable(),
  purpose: z.string().nullable(),
  preparer: z.string().nullable(),
  requestedDate: date,
  accountingDate: date,
  justification: z.string().nullable(),
  lines: z.array(prLineSchema),
  totals: z.array(z.object({ currency: z.string(), amount: z.number() })),
  d365Url: z.string(),
});
export type PrDetail = z.infer<typeof prDetailSchema>;

// ---------- create form ----------

export const prLineFormSchema = z
  .object({
    kind: z.enum(['item', 'category']),
    itemNumber: z.string().trim().optional(),
    procurementCategory: z.string().trim().optional(),
    description: z.string().trim().max(60, 'At most 60 characters.').optional(),
    quantity: z.coerce.number({ invalid_type_error: 'Enter a quantity.' }).gt(0, 'Quantity must be greater than 0.'),
    unit: z.string().trim().optional(),
    unitPrice: z.union([z.literal(''), z.coerce.number().min(0, 'Price can’t be negative.')]).optional(),
    currency: z.string().trim().optional(),
    vendor: z.string().trim().optional(),
    site: z.string().trim().optional(),
    warehouse: z.string().trim().optional(),
  })
  .superRefine((l, ctx) => {
    if (l.kind === 'item' && !l.itemNumber) ctx.addIssue({ code: 'custom', path: ['itemNumber'], message: 'Select an item.' });
    if (l.kind === 'category') {
      if (!l.procurementCategory) ctx.addIssue({ code: 'custom', path: ['procurementCategory'], message: 'Select a category.' });
      if (!l.description) ctx.addIssue({ code: 'custom', path: ['description'], message: 'Describe what you need.' });
    }
  });
/** Editable line: numeric inputs hold raw text until validated. */
export type PrLineForm = Omit<z.input<typeof prLineFormSchema>, 'quantity' | 'unitPrice'> & {
  quantity: number | string;
  unitPrice?: number | string;
};

export const prFormSchema = z.object({
  name: z.string().trim().min(1, 'Name is required.').max(60, 'At most 60 characters.'),
  purpose: z.enum(['Consumption', 'Replenishment']),
  preparer: z.string().trim().min(1, 'Select the preparer (worker).'),
  requestedDate: z.string().optional(),
  accountingDate: z.string().optional(),
  justification: z.string().max(1000).optional(),
});
export type PrForm = z.infer<typeof prFormSchema>;

export const emptyLine = (): PrLineForm => ({ kind: 'item', quantity: 1, unitPrice: '' });

/** Estimated totals per currency while editing (D365 computes the final amounts). */
export function estimateTotals(lines: PrLineForm[], fallbackCurrency = ''): { currency: string; amount: number }[] {
  const map = new Map<string, number>();
  for (const l of lines) {
    const qty = Number(l.quantity) || 0;
    const price = l.unitPrice === '' || l.unitPrice === undefined ? 0 : Number(l.unitPrice) || 0;
    const cur = (l.currency || fallbackCurrency).toUpperCase();
    map.set(cur, (map.get(cur) ?? 0) + qty * price);
  }
  return [...map.entries()].map(([currency, amount]) => ({ currency, amount: Math.round(amount * 100) / 100 }));
}

export function toCreateBody(form: PrForm, lines: PrLineForm[]) {
  const blank = (v?: string) => (v && v.trim() ? v.trim() : null);
  return {
    name: form.name.trim(),
    purpose: form.purpose,
    preparer: form.preparer.trim(),
    requestedDate: blank(form.requestedDate),
    accountingDate: blank(form.accountingDate),
    justification: blank(form.justification),
    lines: lines.map((l) => ({
      itemNumber: l.kind === 'item' ? blank(l.itemNumber) : null,
      procurementCategory: blank(l.procurementCategory),
      description: blank(l.description),
      quantity: Number(l.quantity),
      unit: blank(l.unit),
      unitPrice: l.unitPrice === '' || l.unitPrice === undefined ? null : Number(l.unitPrice),
      currency: blank(l.currency),
      vendor: blank(l.vendor),
      site: blank(l.site),
      warehouse: blank(l.warehouse),
      requestedDate: null,
    })),
  };
}

// ---------- API ----------

export async function listPurchaseRequisitions(
  api: ApiRequest,
  p: { search?: string; status?: string; top?: number; skip?: number },
  signal?: AbortSignal,
): Promise<PrList> {
  const qs = new URLSearchParams();
  if (p.search?.trim()) qs.set('search', p.search.trim());
  if (p.status) qs.set('status', p.status);
  qs.set('top', String(p.top ?? 25));
  qs.set('skip', String(p.skip ?? 0));
  return prListSchema.parse(await api<unknown>(`/purchase-requisitions?${qs}`, { signal }));
}

export async function getPurchaseRequisition(api: ApiRequest, number: string): Promise<PrDetail> {
  return prDetailSchema.parse(await api<unknown>(`/purchase-requisitions/${encodeURIComponent(number)}`));
}

export async function createPurchaseRequisition(api: ApiRequest, body: ReturnType<typeof toCreateBody>): Promise<PrDetail> {
  return prDetailSchema.parse(await api<unknown>('/purchase-requisitions', { method: 'POST', body }));
}
