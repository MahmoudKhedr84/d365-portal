import { z } from 'zod';
import type { ApiRequest } from './client';

export const auditEntrySchema = z.object({
  id: z.number(),
  timestamp: z.string(),
  tenantId: z.string().nullable(),
  userEmail: z.string().nullable(),
  company: z.string().nullable(),
  entity: z.string(),
  action: z.string(),
  requestSummary: z.string().nullable(),
  responseSummary: z.string().nullable(),
  statusCode: z.number().nullable(),
  success: z.boolean(),
  durationMs: z.number().nullable(),
  correlationId: z.string().nullable(),
});
export type AuditEntry = z.infer<typeof auditEntrySchema>;
export const auditPageSchema = z.object({ items: z.array(auditEntrySchema), total: z.number() });

export interface AuditFilter {
  search?: string;
  success?: '' | 'true' | 'false';
  action?: string;
  top?: number;
  skip?: number;
}

/** base: "/admin" for a tenant admin, "/platform/tenants/{id}" for a super-admin. */
export async function getAudit(api: ApiRequest, base: string, tenantScoped: boolean, f: AuditFilter, signal?: AbortSignal) {
  const qs = new URLSearchParams();
  if (f.search?.trim()) qs.set('search', f.search.trim());
  if (f.success) qs.set('success', f.success);
  if (f.action) qs.set('action', f.action);
  qs.set('top', String(f.top ?? 50));
  qs.set('skip', String(f.skip ?? 0));
  return auditPageSchema.parse(await api<unknown>(`${base}/audit?${qs}`, { tenantScoped, signal }));
}
