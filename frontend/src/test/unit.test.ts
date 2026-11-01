import { describe, expect, it, vi } from 'vitest';
import { parseConfig } from '@/config';
import { ApiError, createApiClient } from '@/api/client';
import { pickInitialTenant } from '@/state/sessionStore';
import { canSee, NAV_ITEMS, visibleNav } from '@/navigation';
import { directionFor } from '@/i18n';

import { makeMe, T1, T2 } from './fixtures';

describe('parseConfig', () => {
  it('accepts a complete config and applies defaults', () => {
    const r = parseConfig({ entraClientId: T1, apiScope: `api://${T2}/access_as_user` });
    expect(r.ok).toBe(true);
    if (r.ok) {
      expect(r.config.entraAuthority).toBe('https://login.microsoftonline.com/organizations');
      expect(r.config.apiBaseUrl).toBe('/api');
    }
  });

  it('reports missing values (blank counts as missing)', () => {
    const r = parseConfig({ entraClientId: '  ', apiScope: '' });
    expect(r.ok).toBe(false);
    if (!r.ok) expect(r.errors.join('\n')).toMatch(/entraClientId/);
  });
});

describe('api client', () => {
  const okJson = (body: unknown, status = 200) =>
    new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

  it('sends bearer token and tenant header', async () => {
    const fetchImpl = vi.fn(async () => okJson({ ok: true }));
    const api = createApiClient({ baseUrl: '/api/', getToken: async () => 'tok', getTenantId: () => T1, fetchImpl });
    await api('/tenant/current');
    const [url, init] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('/api/tenant/current');
    const headers = init.headers as Record<string, string>;
    expect(headers.Authorization).toBe('Bearer tok');
    expect(headers['X-Tenant-Id']).toBe(T1);
  });

  it('omits the tenant header when tenantScoped is false', async () => {
    const fetchImpl = vi.fn(async () => okJson({}));
    const api = createApiClient({ baseUrl: '/api', getToken: async () => 'tok', getTenantId: () => T1, fetchImpl });
    await api('/session', { method: 'POST', tenantScoped: false });
    const headers = (fetchImpl.mock.calls[0] as unknown as [string, RequestInit])[1].headers as Record<string, string>;
    expect(headers['X-Tenant-Id']).toBeUndefined();
  });

  it('turns problem+json into ApiError with code and detail', async () => {
    const fetchImpl = vi.fn(async () =>
      okJson({ status: 403, code: 'tenant_access_denied', detail: 'You are not an active user of this tenant.' }, 403),
    );
    const api = createApiClient({ baseUrl: '/api', getToken: async () => 'tok', getTenantId: () => T1, fetchImpl });
    await expect(api('/tenant/current')).rejects.toMatchObject({
      status: 403,
      code: 'tenant_access_denied',
      message: 'You are not an active user of this tenant.',
    });
  });

  it('maps network failures to a readable error', async () => {
    const fetchImpl = vi.fn(async () => {
      throw new TypeError('Failed to fetch');
    });
    const api = createApiClient({ baseUrl: '/api', getToken: async () => 'tok', getTenantId: () => null, fetchImpl });
    const err = await api('/me').catch((e: unknown) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect((err as ApiError).code).toBe('network_error');
  });
});

describe('pickInitialTenant', () => {
  it('auto-selects the only tenant', () => {
    expect(pickInitialTenant(makeMe([{}]), null)).toBe(T1);
  });
  it('keeps a stored tenant that is still valid', () => {
    expect(pickInitialTenant(makeMe([{}, {}]), T2)).toBe(T2);
  });
  it('asks (null) when several tenants and no valid stored choice', () => {
    expect(pickInitialTenant(makeMe([{}, {}]), 'gone')).toBeNull();
  });
  it('returns null when there are no tenants', () => {
    expect(pickInitialTenant(makeMe([]), T1)).toBeNull();
  });
});

describe('navigation visibility', () => {
  it('users see workspaces only', () => {
    const paths = visibleNav({ role: 'User', isSuperAdmin: false }).map((i) => i.path);
    expect(paths).toContain('/purchase-orders');
    expect(paths).not.toContain('/admin/users');
    expect(paths).not.toContain('/platform/tenants');
  });
  it('tenant admins also see administration', () => {
    expect(visibleNav({ role: 'TenantAdmin', isSuperAdmin: false }).map((i) => i.path)).toContain('/admin/users');
  });
  it('super-admin without a tenant sees only the platform area', () => {
    const items = visibleNav({ role: null, isSuperAdmin: true });
    expect(items.every((i) => canSee(i, { role: null, isSuperAdmin: true }))).toBe(true);
    expect(items.map((i) => i.section)).toEqual(['platform']);
    expect(NAV_ITEMS.length).toBeGreaterThan(items.length);
  });
});

describe('i18n direction', () => {
  it('uses RTL for Arabic', () => {
    expect(directionFor('ar')).toBe('rtl');
    expect(directionFor('ar-EG')).toBe('rtl');
    expect(directionFor('en')).toBe('ltr');
  });
});
