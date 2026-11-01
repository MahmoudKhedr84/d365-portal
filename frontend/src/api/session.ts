import { z } from 'zod';
import type { ApiRequest } from './client';

export const roleSchema = z.enum(['User', 'TenantAdmin']);
export type Role = z.infer<typeof roleSchema>;

export const membershipSchema = z.object({
  tenantId: z.string().uuid(),
  tenantName: z.string(),
  tenantSlug: z.string(),
  role: roleSchema,
  defaultCompany: z.string().nullable(),
  currentCompany: z.string().nullable(),
  companies: z.array(z.string()),
});
export type Membership = z.infer<typeof membershipSchema>;

export const meSchema = z.object({
  objectId: z.string(),
  entraTenantId: z.string(),
  email: z.string().nullable(),
  displayName: z.string().nullable(),
  isSuperAdmin: z.boolean(),
  memberships: z.array(membershipSchema),
});
export type Me = z.infer<typeof meSchema>;

export const currentTenantSchema = membershipSchema
  .omit({ role: true })
  .extend({ role: roleSchema, userId: z.string().uuid(), email: z.string(), displayName: z.string() });
export type CurrentTenant = z.infer<typeof currentTenantSchema>;

/** Called once after sign-in: binds first-time users and records the login. */
export async function startSession(api: ApiRequest): Promise<Me> {
  return meSchema.parse(await api<unknown>('/session', { method: 'POST', tenantScoped: false }));
}

export async function getCurrentTenant(api: ApiRequest): Promise<CurrentTenant> {
  return currentTenantSchema.parse(await api<unknown>('/tenant/current'));
}

/** Persists the working company for the selected tenant (must be one of the user's companies). */
export async function switchCompany(api: ApiRequest, company: string): Promise<CurrentTenant> {
  return currentTenantSchema.parse(await api<unknown>('/session/company', { method: 'PUT', body: { company } }));
}
