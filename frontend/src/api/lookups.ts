import { z } from 'zod';
import type { ApiRequest } from './client';

export const lookupItemSchema = z.object({
  id: z.string(),
  name: z.string(),
  extra: z.record(z.string().nullable()).nullable().optional(),
});
export type LookupItem = z.infer<typeof lookupItemSchema>;

export const lookupInfoSchema = z.object({ key: z.string(), label: z.string() });
export type LookupInfo = z.infer<typeof lookupInfoSchema>;

/** Keys served by GET /api/lookups/{key}. */
export type LookupKey =
  | 'vendors'
  | 'products'
  | 'procurementCategories'
  | 'sites'
  | 'warehouses'
  | 'mainAccounts'
  | 'financialDimensions'
  | 'dimensionValues'
  | 'journalNames'
  | 'currencies'
  | 'workers'
  | 'customers'
  | 'caseCategories'
  | 'units';

export function lookupUrl(key: string, search: string, filters: Record<string, string | undefined> = {}, top = 25): string {
  const qs = new URLSearchParams();
  if (search.trim()) qs.set('search', search.trim());
  qs.set('top', String(top));
  for (const [k, v] of Object.entries(filters)) if (v) qs.set(k, v);
  return `/lookups/${encodeURIComponent(key)}?${qs.toString()}`;
}

export async function searchLookup(
  api: ApiRequest,
  key: string,
  search: string,
  filters?: Record<string, string | undefined>,
  signal?: AbortSignal,
): Promise<LookupItem[]> {
  return z.array(lookupItemSchema).parse(await api<unknown>(lookupUrl(key, search, filters), { signal }));
}

export async function listLookups(api: ApiRequest): Promise<LookupInfo[]> {
  return z.array(lookupInfoSchema).parse(await api<unknown>('/lookups'));
}

export const entityCheckRowSchema = z.object({
  key: z.string(),
  label: z.string(),
  kind: z.enum(['lookup', 'transaction']),
  found: z.boolean(),
  entitySet: z.string().nullable(),
  idField: z.string().nullable(),
  nameField: z.string().nullable(),
  companySpecific: z.boolean().nullable(),
  fieldsConfirmed: z.boolean(),
  tried: z.array(z.string()),
  sampleFields: z.array(z.string()),
  error: z.string().nullable(),
});
export type EntityCheckRow = z.infer<typeof entityCheckRowSchema>;
