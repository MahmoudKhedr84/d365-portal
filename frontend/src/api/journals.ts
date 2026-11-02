import { z } from 'zod';
import type { ApiRequest } from './client';

export const ACCOUNT_TYPES = ['Ledger', 'Cust', 'Vend', 'Bank'] as const;
export type AccountType = (typeof ACCOUNT_TYPES)[number];
export const accountTypeLabel = (t: string | null | undefined) =>
  ({ Ledger: 'Ledger', Cust: 'Customer', Vend: 'Vendor', Bank: 'Bank' })[t ?? ''] ?? t ?? '—';
/** Lookup used to pick an account of each type (Ledger = main account; dimensions can be appended). */
export const accountLookup: Record<AccountType, string> = { Ledger: 'mainAccounts', Cust: 'customers', Vend: 'vendors', Bank: 'bankAccounts' };

const date = z.string().nullable();

export const journalListItemSchema = z.object({
  journalBatchNumber: z.string(),
  journalName: z.string().nullable(),
  description: z.string().nullable(),
  posted: z.boolean(),
  totalDebit: z.number(),
  totalCredit: z.number(),
});
export type JournalListItem = z.infer<typeof journalListItemSchema>;
export const journalListSchema = z.object({ items: z.array(journalListItemSchema), total: z.number().nullable() });

export const journalLineSchema = z.object({
  lineNumber: z.number(),
  date,
  voucher: z.string().nullable(),
  accountType: z.string().nullable(),
  account: z.string().nullable(),
  text: z.string().nullable(),
  debit: z.number(),
  credit: z.number(),
  currency: z.string().nullable(),
  offsetAccountType: z.string().nullable(),
  offsetAccount: z.string().nullable(),
});
export const journalDetailSchema = z.object({
  journalBatchNumber: z.string(),
  company: z.string(),
  journalName: z.string().nullable(),
  description: z.string().nullable(),
  posted: z.boolean(),
  lines: z.array(journalLineSchema),
  totals: z.array(z.object({ currency: z.string(), debit: z.number(), credit: z.number(), balance: z.number() })),
  d365Url: z.string(),
});
export type JournalDetail = z.infer<typeof journalDetailSchema>;

export interface JournalLineForm {
  date: string;
  accountType: AccountType;
  account: string;
  /** Ledger only: dimension segments appended to the main account, e.g. "-001-022". */
  dimensions: string;
  text: string;
  debit: string;
  credit: string;
  currency: string;
  offsetAccountType: AccountType | '';
  offsetAccount: string;
  voucher: string;
}

export const today = () => new Date().toISOString().slice(0, 10);
export const emptyJournalLine = (prev?: JournalLineForm): JournalLineForm => ({
  date: prev?.date ?? today(),
  accountType: 'Ledger',
  account: '',
  dimensions: '',
  text: prev?.text ?? '',
  debit: '',
  credit: '',
  currency: prev?.currency ?? '',
  offsetAccountType: '',
  offsetAccount: '',
  voucher: prev?.voucher ?? '',
});

const amount = (v: string) => {
  const n = Number(v);
  return v.trim() === '' || !Number.isFinite(n) ? 0 : n;
};

/** Full account display value: main account plus the optional ledger dimension segments. */
export function accountDisplay(l: Pick<JournalLineForm, 'accountType' | 'account' | 'dimensions'>) {
  const dims = l.dimensions.trim();
  if (l.accountType !== 'Ledger' || !dims) return l.account.trim();
  return `${l.account.trim()}${dims.startsWith('-') ? dims : `-${dims}`}`;
}

export function validateJournalLine(l: JournalLineForm): Record<string, string> {
  const e: Record<string, string> = {};
  if (!l.date) e.date = 'Date is required.';
  if (!l.account.trim()) e.account = 'Account is required.';
  const d = amount(l.debit), c = amount(l.credit);
  if (d < 0 || c < 0) e.amount = 'Amounts can’t be negative.';
  else if ((d > 0) === (c > 0)) e.amount = 'Enter either a debit or a credit.';
  if (!/^[A-Za-z]{3}$/.test(l.currency.trim())) e.currency = 'Currency is required.';
  if (l.offsetAccount.trim() && !l.offsetAccountType) e.offsetAccountType = 'Choose the offset account type.';
  return e;
}

/**
 * Running totals per currency. Lines with an offset account balance themselves, so they count on both sides.
 */
export function journalTotals(lines: JournalLineForm[]) {
  const map = new Map<string, { debit: number; credit: number }>();
  for (const l of lines) {
    const cur = l.currency.trim().toUpperCase() || '—';
    const t = map.get(cur) ?? { debit: 0, credit: 0 };
    const d = amount(l.debit), c = amount(l.credit);
    t.debit += d; t.credit += c;
    if (l.offsetAccount.trim()) { t.debit += c; t.credit += d; }
    map.set(cur, t);
  }
  return [...map.entries()].map(([currency, t]) => ({
    currency,
    debit: Math.round(t.debit * 100) / 100,
    credit: Math.round(t.credit * 100) / 100,
    balance: Math.round((t.debit - t.credit) * 100) / 100,
  }));
}

export function toJournalBody(journalName: string, description: string, lines: JournalLineForm[]) {
  const blank = (v: string) => (v.trim() ? v.trim() : null);
  return {
    journalName: journalName.trim(),
    description: blank(description),
    lines: lines.map((l) => ({
      date: l.date,
      accountType: l.accountType,
      account: accountDisplay(l),
      text: blank(l.text),
      debit: amount(l.debit) > 0 ? amount(l.debit) : null,
      credit: amount(l.credit) > 0 ? amount(l.credit) : null,
      currency: l.currency.trim().toUpperCase(),
      offsetAccountType: l.offsetAccount.trim() ? l.offsetAccountType || null : null,
      offsetAccount: blank(l.offsetAccount),
      voucher: blank(l.voucher),
      document: null,
    })),
  };
}

export async function listJournals(api: ApiRequest, p: { search?: string; posted?: string; top?: number; skip?: number }, signal?: AbortSignal) {
  const qs = new URLSearchParams();
  if (p.search?.trim()) qs.set('search', p.search.trim());
  if (p.posted) qs.set('posted', p.posted);
  qs.set('top', String(p.top ?? 25));
  qs.set('skip', String(p.skip ?? 0));
  return journalListSchema.parse(await api<unknown>(`/general-journals?${qs}`, { signal }));
}
export async function getJournal(api: ApiRequest, number: string) {
  return journalDetailSchema.parse(await api<unknown>(`/general-journals/${encodeURIComponent(number)}`));
}
export async function createJournal(api: ApiRequest, body: ReturnType<typeof toJournalBody>) {
  return journalDetailSchema.parse(await api<unknown>('/general-journals', { method: 'POST', body }));
}
