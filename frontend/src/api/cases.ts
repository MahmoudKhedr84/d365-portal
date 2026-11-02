import { z } from 'zod';
import type { ApiRequest } from './client';

export const CASE_STATUSES = ['Planned', 'InProcess', 'Closed', 'Cancelled'] as const;
export const CASE_CLASSIFICATIONS = ['None', 'Corrective', 'Preventive', 'CorrectiveAndPreventive'] as const;
export const caseStatusLabel = (s: string | null | undefined) => (s === 'InProcess' ? 'In process' : (s ?? '—'));
export const classificationLabel = (s: string | null | undefined) =>
  s === 'CorrectiveAndPreventive' ? 'Corrective and preventive' : (s ?? '—');

const date = z.string().nullable();

export const caseListItemSchema = z.object({
  caseId: z.string(),
  description: z.string().nullable(),
  status: z.string().nullable(),
  classification: z.string().nullable(),
  category: z.string().nullable(),
  type: z.string().nullable(),
  priority: z.string().nullable(),
  dueDate: date,
  responsible: z.string().nullable(),
});
export type CaseListItem = z.infer<typeof caseListItemSchema>;
export const caseListSchema = z.object({ items: z.array(caseListItemSchema), total: z.number().nullable() });

export const caseDetailSchema = z.object({
  caseId: z.string(),
  company: z.string(),
  description: z.string().nullable(),
  name: z.string().nullable(),
  status: z.string().nullable(),
  classification: z.string().nullable(),
  category: z.string().nullable(),
  subCategory: z.string().nullable(),
  type: z.string().nullable(),
  source: z.string().nullable(),
  priority: z.string().nullable(),
  dueDate: date,
  plannedEffectiveDate: date,
  responsible: z.string().nullable(),
  responsibleName: z.string().nullable(),
  notes: z.string().nullable(),
  createdAt: z.string().nullable(),
  d365Url: z.string(),
});
export type CaseDetail = z.infer<typeof caseDetailSchema>;

export const caseFormSchema = z.object({
  description: z.string().trim().min(1, 'Description is required.').max(60, 'At most 60 characters.'),
  classification: z.enum(CASE_CLASSIFICATIONS),
  category: z.string().optional(),
  type: z.string().optional(),
  source: z.string().optional(),
  priority: z.string().max(20, 'At most 20 characters.').optional(),
  dueDate: z.string().optional(),
  plannedEffectiveDate: z.string().optional(),
  responsible: z.string().optional(),
  notes: z.string().max(4000).optional(),
});
export type CaseForm = z.infer<typeof caseFormSchema>;

export function toCaseBody(f: CaseForm) {
  const blank = (v?: string) => (v && v.trim() ? v.trim() : null);
  return {
    description: f.description.trim(),
    classification: f.classification,
    category: blank(f.category),
    type: blank(f.type),
    source: blank(f.source),
    priority: blank(f.priority),
    dueDate: blank(f.dueDate),
    plannedEffectiveDate: blank(f.plannedEffectiveDate),
    responsible: blank(f.responsible),
    notes: blank(f.notes),
  };
}

export async function listCases(api: ApiRequest, p: { search?: string; status?: string; top?: number; skip?: number }, signal?: AbortSignal) {
  const qs = new URLSearchParams();
  if (p.search?.trim()) qs.set('search', p.search.trim());
  if (p.status) qs.set('status', p.status);
  qs.set('top', String(p.top ?? 25));
  qs.set('skip', String(p.skip ?? 0));
  return caseListSchema.parse(await api<unknown>(`/cases?${qs}`, { signal }));
}
export async function getCase(api: ApiRequest, id: string) {
  return caseDetailSchema.parse(await api<unknown>(`/cases/${encodeURIComponent(id)}`));
}
export async function createCase(api: ApiRequest, body: ReturnType<typeof toCaseBody>) {
  return caseDetailSchema.parse(await api<unknown>('/cases', { method: 'POST', body }));
}
