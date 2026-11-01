import { z } from 'zod';
import { entityCheckRowSchema, type EntityCheckRow } from './lookups';
import type { ApiRequest } from './client';
import { roleSchema, type Role } from './session';

export const connectionSchema = z.object({
  environmentUrl: z.string(),
  entraTenantId: z.string(),
  clientId: z.string(),
  hasClientSecret: z.boolean(),
  apiVersion: z.string().nullable(),
  isActive: z.boolean(),
  lastTestedAt: z.string().nullable(),
  lastTestSucceeded: z.boolean().nullable(),
  lastTestResult: z.string().nullable(),
  updatedAt: z.string(),
});
export type Connection = z.infer<typeof connectionSchema>;

export const tenantSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  slug: z.string(),
  isActive: z.boolean(),
  createdAt: z.string(),
  allowedEntraTenantIds: z.array(z.string()),
  userCount: z.number(),
  connection: connectionSchema.nullable(),
});
export type Tenant = z.infer<typeof tenantSchema>;

export const userSchema = z.object({
  id: z.string().uuid(),
  email: z.string(),
  displayName: z.string(),
  role: roleSchema,
  defaultCompany: z.string().nullable(),
  companies: z.array(z.string()),
  isActive: z.boolean(),
  isSignedInOnce: z.boolean(),
  createdAt: z.string(),
  lastLoginAt: z.string().nullable(),
});
export type User = z.infer<typeof userSchema>;

export const companySchema = z.object({ id: z.string(), name: z.string() });
export type Company = z.infer<typeof companySchema>;

export const testResultSchema = z.object({
  success: z.boolean(),
  message: z.string(),
  durationMs: z.number(),
  testedAt: z.string(),
});
export type TestResult = z.infer<typeof testResultSchema>;

// ---------- Form schemas (client-side validation, mirrors the API's FluentValidation) ----------

const guid = z.string().trim().uuid('Must be a GUID (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx).');

export const connectionFormSchema = z.object({
  environmentUrl: z
    .string()
    .trim()
    .min(1, 'Environment URL is required.')
    .refine((v) => /^(https:\/\/)?[a-z0-9.-]+\.dynamics\.com\/?$/i.test(v.replace(/\?.*$/, '')), {
      message: 'Use the environment URL, e.g. https://contoso.operations.dynamics.com',
    }),
  entraTenantId: guid,
  clientId: guid,
  clientSecret: z.string().optional(),
  apiVersion: z.string().trim().max(32).optional(),
  isActive: z.boolean(),
});
export type ConnectionForm = z.infer<typeof connectionFormSchema>;

export const companyCode = z.string().trim().regex(/^[A-Za-z0-9]{1,8}$/, 'Company codes are 1–8 letters/digits.');

export const userFormSchema = z
  .object({
    email: z.string().trim().email('Enter a valid email (the user’s Microsoft sign-in name).'),
    displayName: z.string().trim().max(200).optional(),
    role: roleSchema,
    companies: z.array(companyCode),
    defaultCompany: z.string().nullable(),
    isActive: z.boolean(),
  })
  .refine((v) => v.defaultCompany === null || v.companies.includes(v.defaultCompany), {
    message: 'Default company must be one of the allowed companies.',
    path: ['defaultCompany'],
  });
export type UserForm = z.infer<typeof userFormSchema>;

export const tenantFormSchema = z.object({
  name: z.string().trim().min(1, 'Name is required.').max(200),
  slug: z
    .string()
    .trim()
    .regex(/^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$/, 'Lowercase letters, digits and hyphens (e.g. contoso-eg).'),
  allowedEntraTenantIds: z.array(guid),
});
export type TenantForm = z.infer<typeof tenantFormSchema>;

/** "Acme Egypt" → "acme-egypt" */
export const slugify = (name: string) =>
  name
    .toLowerCase()
    .normalize('NFKD')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 64);

// ---------- Endpoint scope: the same screens work for a tenant admin and for a super-admin managing a tenant ----------

export interface AdminScope {
  /** Query-key prefix, unique per scope. */
  key: readonly unknown[];
  getConnection(): Promise<Connection | null>;
  saveConnection(form: ConnectionForm): Promise<Connection>;
  testConnection(): Promise<TestResult>;
  getCompanies(): Promise<Company[]>;
  /** Probes the standard D365 entities the portal uses and reports the names found. */
  checkEntities(): Promise<EntityCheckRow[]>;
  /** Entity set names in the environment containing the search text. */
  findEntitySets(search: string): Promise<string[]>;
  listUsers(): Promise<User[]>;
  createUser(form: UserForm): Promise<User>;
  updateUser(id: string, patch: Partial<UserForm>): Promise<User>;
}

function makeScope(api: ApiRequest, base: string, key: readonly unknown[], tenantScoped: boolean): AdminScope {
  const opts = { tenantScoped };
  return {
    key,
    async getConnection() {
      const res = await api<unknown>(`${base}/connection`, opts);
      return res ? connectionSchema.parse(res) : null; // 204 → undefined
    },
    async saveConnection(form) {
      const body = {
        ...form,
        clientSecret: form.clientSecret?.trim() ? form.clientSecret.trim() : null,
        apiVersion: form.apiVersion?.trim() ? form.apiVersion.trim() : null,
      };
      return connectionSchema.parse(await api<unknown>(`${base}/connection`, { ...opts, method: 'PUT', body }));
    },
    async testConnection() {
      return testResultSchema.parse(await api<unknown>(`${base}/connection/test`, { ...opts, method: 'POST' }));
    },
    async getCompanies() {
      return z.array(companySchema).parse(await api<unknown>(`${base}/companies`, opts));
    },
    async checkEntities() {
      return z.array(entityCheckRowSchema).parse(await api<unknown>(`${base}/d365/entities`, opts));
    },
    async findEntitySets(search) {
      return z.array(z.string()).parse(
        await api<unknown>(`${base}/d365/entity-sets?search=${encodeURIComponent(search)}`, opts),
      );
    },
    async listUsers() {
      return z.array(userSchema).parse(await api<unknown>(`${base}/users`, opts));
    },
    async createUser(form) {
      const body = { ...form, displayName: form.displayName || null };
      return userSchema.parse(await api<unknown>(`${base}/users`, { ...opts, method: 'POST', body }));
    },
    async updateUser(id, patch) {
      return userSchema.parse(await api<unknown>(`${base}/users/${id}`, { ...opts, method: 'PATCH', body: patch }));
    },
  };
}

/** Tenant admin of the currently selected tenant (X-Tenant-Id). */
export const tenantAdminScope = (api: ApiRequest, tenantId: string) =>
  makeScope(api, '/admin', ['admin', tenantId], true);

/** Super-admin managing any tenant (no tenant header needed). */
export const platformTenantScope = (api: ApiRequest, tenantId: string) =>
  makeScope(api, `/platform/tenants/${tenantId}`, ['platform', tenantId], false);

// ---------- Platform: tenants ----------

export async function listTenants(api: ApiRequest): Promise<Tenant[]> {
  return z.array(tenantSchema).parse(await api<unknown>('/platform/tenants', { tenantScoped: false }));
}

export async function getTenant(api: ApiRequest, id: string): Promise<Tenant> {
  return tenantSchema.parse(await api<unknown>(`/platform/tenants/${id}`, { tenantScoped: false }));
}

export async function createTenant(api: ApiRequest, form: TenantForm): Promise<Tenant> {
  return tenantSchema.parse(await api<unknown>('/platform/tenants', { tenantScoped: false, method: 'POST', body: form }));
}

export async function updateTenant(
  api: ApiRequest,
  id: string,
  patch: { name?: string; isActive?: boolean; allowedEntraTenantIds?: string[] },
): Promise<Tenant> {
  return tenantSchema.parse(await api<unknown>(`/platform/tenants/${id}`, { tenantScoped: false, method: 'PATCH', body: patch }));
}

export const roleLabel = (r: Role) => (r === 'TenantAdmin' ? 'Tenant admin' : 'User');

/** Permanently deletes a tenant. The slug must be repeated as confirmation. */
export async function deleteTenant(api: ApiRequest, id: string, confirmSlug: string): Promise<void> {
  await api<void>(`/platform/tenants/${id}?confirm=${encodeURIComponent(confirmSlug)}`, { tenantScoped: false, method: 'DELETE' });
}
