import { z } from 'zod';
import type { ApiRequest } from './client';
import { lineFromDetail, type PrLineForm } from './purchaseRequisitions';

export const PO_STATUSES = ['Backorder', 'Received', 'Invoiced', 'Canceled'] as const;
export const PO_APPROVALS = ['Draft', 'InReview', 'Approved', 'Rejected', 'Confirmed', 'Finalized'] as const;

export const poStatusLabel = (s: string | null | undefined) =>
  s === 'Backorder' ? 'Open order' : s === 'Canceled' ? 'Canceled' : (s ?? '—');
export const poApprovalLabel = (s: string | null | undefined) =>
  s === 'InReview' ? 'In review' : s === 'InExternalReview' ? 'In external review' : (s ?? '—');

const date = z.string().nullable();

export const poListItemSchema = z.object({
  purchaseOrderNumber: z.string(),
  vendor: z.string().nullable(),
  vendorName: z.string().nullable(),
  status: z.string().nullable(),
  approvalStatus: z.string().nullable(),
  currency: z.string().nullable(),
  requestedDeliveryDate: date,
  vendorReference: z.string().nullable(),
});
export type PoListItem = z.infer<typeof poListItemSchema>;
export const poListSchema = z.object({ items: z.array(poListItemSchema), total: z.number().nullable() });

export const poLineSchema = z.object({
  lineNumber: z.number(),
  itemNumber: z.string().nullable(),
  procurementCategory: z.string().nullable(),
  variant: z.string().nullable(),
  description: z.string().nullable(),
  quantity: z.number(),
  unit: z.string().nullable(),
  unitPrice: z.number(),
  lineAmount: z.number(),
  site: z.string().nullable(),
  warehouse: z.string().nullable(),
  requestedDeliveryDate: date,
  status: z.string().nullable(),
  salesTaxGroup: z.string().nullable().optional(),
  itemSalesTaxGroup: z.string().nullable().optional(),
  location: z.string().nullable().optional(),
  inventoryStatus: z.string().nullable().optional(),
  configuration: z.string().nullable().optional(),
  size: z.string().nullable().optional(),
  color: z.string().nullable().optional(),
  style: z.string().nullable().optional(),
  version: z.string().nullable().optional(),
});
export type PoLine = z.infer<typeof poLineSchema>;

export const poDetailSchema = z.object({
  purchaseOrderNumber: z.string(),
  company: z.string(),
  vendor: z.string().nullable(),
  vendorName: z.string().nullable(),
  status: z.string().nullable(),
  approvalStatus: z.string().nullable(),
  currency: z.string().nullable(),
  requestedDeliveryDate: date,
  accountingDate: date,
  site: z.string().nullable(),
  warehouse: z.string().nullable(),
  vendorReference: z.string().nullable(),
  requester: z.string().nullable(),
  salesTaxGroup: z.string().nullable().optional(),
  lines: z.array(poLineSchema),
  total: z.number(),
  d365Url: z.string(),
  /** Only orders whose approval status is Draft can be changed. */
  canEdit: z.boolean().optional(),
});
export type PoDetail = z.infer<typeof poDetailSchema>;

export const poFormSchema = z.object({
  vendor: z.string().trim().min(1, 'Select a vendor.'),
  currency: z.string().trim().optional(),
  requestedDeliveryDate: z.string().optional(),
  accountingDate: z.string().optional(),
  site: z.string().optional(),
  warehouse: z.string().optional(),
  vendorReference: z.string().max(60, 'At most 60 characters.').optional(),
  requester: z.string().optional(),
  salesTaxGroup: z.string().optional(),
  /** Only when the company's PO number sequence is manual. */
  purchaseOrderNumber: z.string().trim().max(20, 'At most 20 characters.').optional(),
});
export type PoForm = z.infer<typeof poFormSchema>;

export function toPoCreateBody(form: PoForm, lines: PrLineForm[]) {
  const blank = (v?: string) => (v && v.trim() ? v.trim() : null);
  return {
    vendor: form.vendor.trim(),
    currency: blank(form.currency),
    requestedDeliveryDate: blank(form.requestedDeliveryDate),
    accountingDate: blank(form.accountingDate),
    site: blank(form.site),
    warehouse: blank(form.warehouse),
    vendorReference: blank(form.vendorReference),
    requester: blank(form.requester),
    salesTaxGroup: blank(form.salesTaxGroup),
    purchaseOrderNumber: blank(form.purchaseOrderNumber),
    lines: lines.map((l) => {
      const item = l.kind === 'item';
      return {
        itemNumber: item ? blank(l.itemNumber) : null,
        procurementCategory: item ? null : blank(l.procurementCategory),
        description: blank(l.description),
        quantity: Number(l.quantity),
        unit: blank(l.unit),
        unitPrice: l.unitPrice === '' || l.unitPrice === undefined ? null : Number(l.unitPrice),
        site: blank(l.site),
        warehouse: blank(l.warehouse),
        location: item ? blank(l.location) : null,
        inventoryStatus: item ? blank(l.inventoryStatus) : null,
        lineNumber: l.lineNumber ?? null,
        requestedDeliveryDate: null,
        configuration: item ? blank(l.configuration) : null,
        size: item ? blank(l.size) : null,
        color: item ? blank(l.color) : null,
        style: item ? blank(l.style) : null,
        version: item ? blank(l.version) : null,
        salesTaxGroup: blank(l.salesTaxGroup),
        itemSalesTaxGroup: blank(l.itemSalesTaxGroup),
      };
    }),
  };
}

/** Form state for editing an existing purchase order (approval status Draft). */
export function poFromDetail(d: PoDetail): { form: PoForm; lines: PrLineForm[] } {
  const v = (x?: string | null) => x ?? '';
  return {
    form: {
      vendor: v(d.vendor),
      currency: v(d.currency),
      requestedDeliveryDate: v(d.requestedDeliveryDate),
      accountingDate: v(d.accountingDate),
      site: v(d.site),
      warehouse: v(d.warehouse),
      vendorReference: v(d.vendorReference),
      requester: v(d.requester),
      salesTaxGroup: v(d.salesTaxGroup),
      purchaseOrderNumber: '',
    },
    lines: d.lines.map((l) => lineFromDetail({ ...l, productName: null }, d.currency, null)),
  };
}

export async function listPurchaseOrders(
  api: ApiRequest,
  p: { search?: string; status?: string; approval?: string; top?: number; skip?: number },
  signal?: AbortSignal,
) {
  const qs = new URLSearchParams();
  if (p.search?.trim()) qs.set('search', p.search.trim());
  if (p.status) qs.set('status', p.status);
  if (p.approval) qs.set('approval', p.approval);
  qs.set('top', String(p.top ?? 25));
  qs.set('skip', String(p.skip ?? 0));
  return poListSchema.parse(await api<unknown>(`/purchase-orders?${qs}`, { signal }));
}

export async function getPurchaseOrder(api: ApiRequest, number: string): Promise<PoDetail> {
  return poDetailSchema.parse(await api<unknown>(`/purchase-orders/${encodeURIComponent(number)}`));
}

export async function updatePurchaseOrder(api: ApiRequest, number: string, body: ReturnType<typeof toPoCreateBody>): Promise<PoDetail> {
  return poDetailSchema.parse(await api<unknown>(`/purchase-orders/${encodeURIComponent(number)}`, { method: 'PUT', body }));
}

export async function createPurchaseOrder(api: ApiRequest, body: ReturnType<typeof toPoCreateBody>): Promise<PoDetail> {
  return poDetailSchema.parse(await api<unknown>('/purchase-orders', { method: 'POST', body }));
}
