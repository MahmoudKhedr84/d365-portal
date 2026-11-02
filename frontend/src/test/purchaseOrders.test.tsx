import { describe, expect, it, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { ApiProvider } from '@/api/ApiProvider';
import type { ApiRequest } from '@/api/client';
import { toPoCreateBody, type PoDetail } from '@/api/purchaseOrders';
import { AppToaster } from '@/components/notify';
import { PurchaseOrderCreatePage } from '@/pages/purchaseOrders/PurchaseOrderCreatePage';
import { PurchaseOrderDetailPage } from '@/pages/purchaseOrders/PurchaseOrderDetailPage';
import { PurchaseOrdersPage } from '@/pages/purchaseOrders/PurchaseOrdersPage';
import { useSessionStore } from '@/state/sessionStore';
import { makeMe } from './fixtures';

const detail: PoDetail = {
  purchaseOrderNumber: 'PO-000007', company: 'WFQ', vendor: 'V1', vendorName: 'Fabrikam', status: 'Backorder', approvalStatus: 'Draft',
  currency: 'EGP', requestedDeliveryDate: '2026-11-20', accountingDate: null, site: 'S1', warehouse: 'W1', vendorReference: 'REF-9',
  requester: null,
  lines: [{ lineNumber: 1, itemNumber: 'SP-OL-0001', procurementCategory: null, variant: 'Acdelco', description: 'Engine oil', quantity: 4,
    unit: 'L', unitPrice: 250, lineAmount: 1000, site: 'S1', warehouse: 'W1', requestedDeliveryDate: null, status: 'Backorder' }],
  total: 1000, d365Url: 'https://x.operations.dynamics.com/?cmp=wfq&mi=PurchTableListPage',
};

type Call = { path: string; method?: string; body?: unknown };

function renderAt(path: string, handler: (c: Call) => unknown) {
  const calls: Call[] = [];
  const api = (async (p: string, o: { method?: string; body?: unknown } = {}) => {
    const c = { path: p, method: o.method, body: o.body };
    calls.push(c);
    return handler(c);
  }) as unknown as ApiRequest;
  render(
    <FluentProvider theme={webLightTheme}>
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
        <AppToaster />
        <ApiProvider baseUrl="/api" client={api}>
          <MemoryRouter initialEntries={[path]}>
            <Routes>
              <Route path="/purchase-orders" element={<PurchaseOrdersPage />} />
              <Route path="/purchase-orders/new" element={<PurchaseOrderCreatePage />} />
              <Route path="/purchase-orders/:number" element={<PurchaseOrderDetailPage />} />
            </Routes>
          </MemoryRouter>
        </ApiProvider>
      </QueryClientProvider>
    </FluentProvider>,
  );
  return calls;
}

beforeEach(() => {
  sessionStorage.clear();
  useSessionStore.getState().reset();
  useSessionStore.getState().setMe(makeMe([{ companies: ['WFQ'], defaultCompany: 'WFQ' }]));
});

describe('purchase orders', () => {
  it('builds the API body with header defaults left to D365', () => {
    const body = toPoCreateBody({ vendor: ' V1 ', currency: '', site: 'S1' }, [
      { kind: 'item', itemNumber: 'SP-OL-0001', quantity: '4', unitPrice: '250', style: 'Acdelco', vendor: 'ignored' },
      { kind: 'category', procurementCategory: 'Services', description: 'Oil change', quantity: 1, unitPrice: '', style: 'x' },
    ]);
    expect(body).toMatchObject({ vendor: 'V1', currency: null, site: 'S1' });
    expect(body.lines[0]).toMatchObject({ itemNumber: 'SP-OL-0001', quantity: 4, unitPrice: 250, style: 'Acdelco' });
    expect(body.lines[1]).toMatchObject({ itemNumber: null, procurementCategory: 'Services', unitPrice: null, style: null });
    expect(body.lines[0]).not.toHaveProperty('vendor');
  });

  it('lists purchase orders of the current company with filters', async () => {
    const calls = renderAt('/purchase-orders', () => ({
      items: [{ purchaseOrderNumber: 'PO-000007', vendor: 'V1', vendorName: 'Fabrikam', status: 'Backorder', approvalStatus: 'Draft',
        currency: 'EGP', requestedDeliveryDate: null, vendorReference: null }],
      total: 1,
    }));
    expect(await screen.findByRole('link', { name: 'PO-000007' })).toBeInTheDocument();
    expect(screen.getAllByText('Open order').length).toBeGreaterThan(0);
    await userEvent.click(screen.getByRole('combobox', { name: 'Approval' }));
    await userEvent.click(await screen.findByRole('option', { name: 'Approved' }));
    await waitFor(() => expect(calls.some((c) => c.path.includes('approval=Approved'))).toBe(true));
  });

  it('shows the order, its lines with variants, total and D365 link', async () => {
    renderAt('/purchase-orders/PO-000007', () => detail);
    expect(await screen.findByRole('heading', { name: 'PO-000007' })).toBeInTheDocument();
    expect(screen.getByText('Acdelco')).toBeInTheDocument();
    expect(screen.getByLabelText('Total')).toHaveTextContent('Total 1,000.00 EGP');
    expect(screen.getByRole('link', { name: /Open in D365/ })).toHaveAttribute('href', detail.d365Url);
  });

  it('requires a vendor, then creates the order', async () => {
    const calls = renderAt('/purchase-orders/new', (c) => {
      if (c.path.startsWith('/lookups/vendors')) return [{ id: 'V1', name: 'Fabrikam', extra: { CurrencyCode: 'EGP' } }];
      if (c.path.startsWith('/lookups/products')) return [{ id: 'A0001', name: 'Filter', extra: { PurchaseUnitSymbol: 'ea' } }];
      if (c.path.startsWith('/lookups')) return [];
      if (c.method === 'POST') return detail;
      return detail;
    });
    await userEvent.click(screen.getByRole('button', { name: 'Create purchase order' }));
    expect(await screen.findByText('Select a vendor.')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('combobox', { name: /^Vendor/ }));
    await userEvent.click(await screen.findByRole('option', { name: /V1/ }, { timeout: 3000 }));
    const item = within(screen.getByRole('group', { name: 'Line 1' })).getByRole('combobox', { name: /Item/ });
    await userEvent.click(item);
    await userEvent.click(await screen.findByRole('option', { name: /A0001/ }, { timeout: 3000 }));
    await userEvent.click(screen.getByRole('button', { name: 'Create purchase order' }));

    await waitFor(() => expect(calls.some((c) => c.method === 'POST')).toBe(true));
    expect(calls.find((c) => c.method === 'POST')!.body).toMatchObject({
      vendor: 'V1', currency: 'EGP', lines: [{ itemNumber: 'A0001', unit: 'ea', quantity: 1 }],
    });
    expect(await screen.findByRole('heading', { name: 'PO-000007' })).toBeInTheDocument();
  }, 20000);
});
