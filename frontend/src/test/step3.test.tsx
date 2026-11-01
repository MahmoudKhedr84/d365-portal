import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import { ApiProvider } from '@/api/ApiProvider';
import { createApiClient, type ApiRequest } from '@/api/client';
import type { AdminScope, Tenant } from '@/api/admin';
import type { EntityCheckRow } from '@/api/lookups';
import { lookupUrl } from '@/api/lookups';
import { SessionGate } from '@/components/SessionGate';
import { EntityCheckPanel } from '@/components/admin/EntityCheckPanel';
import { AppToaster } from '@/components/notify';
import { DeleteTenantCard } from '@/pages/platform/TenantDetailPage';
import { pickCompany, useSessionStore } from '@/state/sessionStore';
import { T1, makeMe } from './fixtures';

function wrap(api: ApiRequest, children: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <FluentProvider theme={webLightTheme}>
      <QueryClientProvider client={qc}>
        <AppToaster />
        <ApiProvider baseUrl="/api" client={api}>
          <MemoryRouter>{children}</MemoryRouter>
        </ApiProvider>
      </QueryClientProvider>
    </FluentProvider>,
  );
}

beforeEach(() => {
  sessionStorage.clear();
  useSessionStore.getState().reset();
});

describe('company selection', () => {
  const m = makeMe([{ companies: ['USMF', 'DEMF'], defaultCompany: 'USMF', currentCompany: null }]).memberships[0]!;

  it('prefers the saved current company, then the default, then the first allowed', () => {
    expect(pickCompany({ ...m, currentCompany: 'demf' })).toBe('DEMF');
    expect(pickCompany({ ...m, currentCompany: 'GBSI' })).toBe('USMF');
    expect(pickCompany({ ...m, defaultCompany: null })).toBe('USMF');
    expect(pickCompany({ ...m, companies: [], defaultCompany: null })).toBeNull();
  });

  it('only switches to an allowed company', () => {
    useSessionStore.getState().setMe(makeMe([{ companies: ['USMF', 'DEMF'] }]));
    expect(useSessionStore.getState().company).toBe('USMF');
    expect(useSessionStore.getState().selectCompany('GBSI')).toBe(false);
    expect(useSessionStore.getState().selectCompany('demf')).toBe(true);
    expect(useSessionStore.getState().company).toBe('DEMF');
  });

  it('sends X-Company with tenant-scoped requests only', async () => {
    const fetchImpl = vi.fn(async () => new Response('{}', { status: 200 }));
    const api = createApiClient({ baseUrl: '/api', getToken: async () => 't', getTenantId: () => T1, getCompany: () => 'DEMF', fetchImpl });
    await api('/lookups/vendors');
    await api('/platform/tenants', { tenantScoped: false });
    const headers = (fetchImpl.mock.calls as unknown as [string, RequestInit][]).map(([, init]) => init.headers as Record<string, string>);
    expect(headers[0]!['X-Company']).toBe('DEMF');
    expect(headers[1]!['X-Company']).toBeUndefined();
  });

  it('builds lookup URLs with search and filters', () => {
    expect(lookupUrl('warehouses', ' main ', { site: '1', other: undefined }, 10)).toBe('/lookups/warehouses?search=main&top=10&site=1');
  });

  it('switches company from the top bar and saves it', async () => {
    const calls: { path: string; method?: string; body?: unknown }[] = [];
    const me = makeMe([{ tenantName: 'Contoso', companies: ['USMF', 'DEMF'], defaultCompany: 'USMF' }]);
    const api = (async (path: string, opts: { method?: string; body?: unknown } = {}) => {
      calls.push({ path, method: opts.method, body: opts.body });
      if (path === '/session') return me;
      if (path === '/session/company') {
        const company = (opts.body as { company: string }).company;
        return { ...me.memberships[0], currentCompany: company, userId: T1, email: 'alice@contoso.com', displayName: 'Alice' };
      }
      return [];
    }) as unknown as ApiRequest;

    wrap(api, <SessionGate onSignOut={vi.fn()} />);
    const button = await screen.findByRole('button', { name: 'Company: USMF' });
    await userEvent.click(button);
    await userEvent.click(await screen.findByRole('menuitemradio', { name: 'DEMF' }));

    await waitFor(() => expect(useSessionStore.getState().company).toBe('DEMF'));
    expect(calls).toContainEqual({ path: '/session/company', method: 'PUT', body: { company: 'DEMF' } });
    expect(await screen.findByText('Contoso · company DEMF')).toBeInTheDocument();
  });
});

describe('platform: delete tenant', () => {
  const tenant: Tenant = {
    id: T1, name: 'YELO UAT', slug: 'yelo-uat', isActive: true, createdAt: '2026-11-01T00:00:00Z',
    allowedEntraTenantIds: [], userCount: 2, connection: null,
  } as Tenant;

  it('requires typing the slug before deleting', async () => {
    const api = vi.fn(async () => undefined) as unknown as ApiRequest;
    wrap(api, <DeleteTenantCard tenant={tenant} />);
    await userEvent.click(screen.getByRole('button', { name: 'Delete tenant' }));
    const confirm = await screen.findByRole('button', { name: 'Delete permanently' }, { timeout: 10000 });
    expect(confirm).toBeDisabled();
    await userEvent.type(screen.getByLabelText('Tenant slug'), 'yelo-ua');
    expect(confirm).toBeDisabled();
    await userEvent.type(screen.getByLabelText('Tenant slug'), 't');
    expect(confirm).toBeEnabled();
    await userEvent.click(confirm);
    await waitFor(() =>
      expect(api).toHaveBeenCalledWith(`/platform/tenants/${T1}?confirm=yelo-uat`, { tenantScoped: false, method: 'DELETE' }),
    );
  }, 20000);
});

describe('entity check', () => {
  it('lists found, missing and empty entities', async () => {
    const rows: EntityCheckRow[] = [
      { key: 'vendors', label: 'Vendors', kind: 'lookup', found: true, entitySet: 'VendorsV2', idField: 'VendorAccountNumber',
        nameField: 'VendorOrganizationName', companySpecific: true, fieldsConfirmed: true, tried: ['VendorsV3', 'VendorsV2'], sampleFields: [], error: null },
      { key: 'cases', label: 'Cases', kind: 'transaction', found: false, entitySet: null, idField: null, nameField: null,
        companySpecific: null, fieldsConfirmed: false, tried: ['CaseDetails'], sampleFields: [], error: 'not found' },
    ];
    const scope = { key: ['x'], checkEntities: vi.fn(async () => rows) } as unknown as AdminScope;
    wrap((async () => null) as unknown as ApiRequest, <EntityCheckPanel scope={scope} />);
    await userEvent.click(screen.getByRole('button', { name: 'Check D365 entities' }));
    expect(await screen.findByText('VendorsV2')).toBeInTheDocument();
    expect(screen.getByText('tried CaseDetails')).toBeInTheDocument();
    expect(screen.getByText('Missing')).toBeInTheDocument();
    expect(screen.getByText(/1 entity was not found/)).toBeInTheDocument();
  });
});
