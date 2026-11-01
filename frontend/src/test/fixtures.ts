import type { Me } from '@/api/session';

export const T1 = '11111111-1111-4111-8111-111111111111';
export const T2 = '22222222-2222-4222-8222-222222222222';

export function makeMe(memberships: Partial<Me['memberships'][number]>[] = [], isSuperAdmin = false): Me {
  return {
    objectId: 'oid',
    entraTenantId: 'tid',
    email: 'alice@contoso.com',
    displayName: 'Alice',
    isSuperAdmin,
    memberships: memberships.map((m, i) => ({
      tenantId: m.tenantId ?? (i === 0 ? T1 : T2),
      tenantName: m.tenantName ?? `Tenant ${i + 1}`,
      tenantSlug: m.tenantSlug ?? `t${i + 1}`,
      role: m.role ?? 'User',
      defaultCompany: m.defaultCompany ?? 'USMF',
      currentCompany: m.currentCompany ?? null,
      companies: m.companies ?? ['USMF'],
    })),
  };
}

