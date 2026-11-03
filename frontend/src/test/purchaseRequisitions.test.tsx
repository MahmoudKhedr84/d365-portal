import { describe, expect, it, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { ApiProvider } from '@/api/ApiProvider';
import type { ApiRequest } from '@/api/client';
import { estimateTotals, toCreateBody, type PrDetail } from '@/api/purchaseRequisitions';
import { AppToaster } from '@/components/notify';
import { PurchaseRequisitionCreatePage } from '@/pages/purchaseRequisitions/PurchaseRequisitionCreatePage';
import { PurchaseRequisitionDetailPage } from '@/pages/purchaseRequisitions/PurchaseRequisitionDetailPage';
import { PurchaseRequisitionsPage } from '@/pages/purchaseRequisitions/PurchaseRequisitionsPage';
import { useSessionStore } from '@/state/sessionStore';
import { T1, makeMe } from './fixtures';

const detail: PrDetail = {
  requisitionNumber: 'PR-000042', name: 'Office supplies', status: 'Draft', purpose: 'Consumption', preparer: '000123',
  requestedDate: '2026-11-15', accountingDate: null, justification: null,
  lines: [{
    lineNumber: 1, buyingLegalEntity: 'USMF', itemNumber: 'A0001', procurementCategory: null, productName: null,
    description: 'Paper', quantity: 2, unit: 'ea', unitPrice: 10, lineAmount: 20, currency: 'USD', vendor: 'V1',
    site: '1', warehouse: '11', requestedDate: null, status: 'Draft',
  }],
  totals: [{ currency: 'USD', amount: 20 }],
  d365Url: 'https://x.operations.dynamics.com/?cmp=usmf&mi=PurchReqTableListPage',
};

type Call = { path: string; method?: string; body?: unknown };

function renderAt(path: string, handler: (c: Call) => unknown) {
  const calls: Call[] = [];
  const api = (async (p: string, o: { method?: string; body?: unknown } = {}) => {
    const c = { path: p, method: o.method, body: o.body };
    calls.push(c);
    return handler(c);
  }) as unknown as ApiRequest;
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <FluentProvider theme={webLightTheme}>
      <QueryClientProvider client={qc}>
        <AppToaster />
        <ApiProvider baseUrl="/api" client={api}>
          <MemoryRouter initialEntries={[path]}>
            <Routes>
              <Route path="/purchase-requisitions" element={<PurchaseRequisitionsPage />} />
              <Route path="/purchase-requisitions/new" element={<PurchaseRequisitionCreatePage />} />
              <Route path="/purchase-requisitions/:number" element={<PurchaseRequisitionDetailPage />} />
              <Route path="/purchase-requisitions/:number/edit" element={<PurchaseRequisitionCreatePage />} />
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
  localStorage.clear();
  useSessionStore.getState().reset();
  useSessionStore.getState().setMe(makeMe([{ companies: ['USMF'] }]));
});

describe('purchase requisition helpers', () => {
  it('estimates totals per currency and builds the API body', () => {
    const lines = [
      { kind: 'item' as const, itemNumber: 'A1', quantity: '2', unitPrice: '10.5', currency: 'usd' },
      { kind: 'category' as const, procurementCategory: 'Office', description: 'Pens', quantity: 3, unitPrice: '' },
    ];
    expect(estimateTotals(lines)).toEqual([{ currency: 'USD', amount: 21 }, { currency: '', amount: 0 }]);
    const body = toCreateBody({ name: ' PR ', purpose: 'Consumption', requester: '000123', requestedDate: '2026-11-15' }, lines);
    expect(body.name).toBe('PR');
    expect(body.accountingDate).toBeNull();
    expect(body.lines[0]).toMatchObject({ itemNumber: 'A1', quantity: 2, unitPrice: 10.5, currency: 'usd' });
    expect(body.lines[1]).toMatchObject({ itemNumber: null, procurementCategory: 'Office', description: 'Pens', unitPrice: null });
  });
});

describe('purchase requisition pages', () => {
  it('lists requisitions and filters by status', async () => {
    const calls = renderAt('/purchase-requisitions', () => ({
      items: [{ requisitionNumber: 'PR-000042', name: 'Office supplies', status: 'InReview', purpose: 'Consumption', preparer: '000123', requestedDate: null }],
      total: 1,
    }));
    expect(await screen.findByRole('link', { name: 'PR-000042' })).toHaveAttribute('href', '/purchase-requisitions/PR-000042');
    expect(screen.getAllByText('In review').length).toBeGreaterThan(0);
    await userEvent.click(screen.getByRole('combobox', { name: 'Status' }));
    await userEvent.click(await screen.findByRole('option', { name: 'Approved' }));
    await waitFor(() => expect(calls.some((c) => c.path.includes('status=Approved'))).toBe(true));
  });

  it('shows a requisition with its lines, totals and a D365 link', async () => {
    renderAt('/purchase-requisitions/PR-000042', () => detail);
    expect(await screen.findByRole('heading', { name: 'PR-000042' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Open in D365/ })).toHaveAttribute('href', detail.d365Url);
    expect(within(screen.getByLabelText('Totals')).getByText('Total 20.00 USD')).toBeInTheDocument();
    expect(screen.getByText('A0001')).toBeInTheDocument();
  });

  it('validates, then creates the requisition in D365 and opens it', async () => {
    localStorage.setItem(`d365portal.requester.${T1}`, '000123');
    const calls = renderAt('/purchase-requisitions/new', (c) => {
      if (c.path.startsWith('/lookups/products'))
        return [
          { id: 'A0001', name: 'Printer paper', extra: { PurchaseUnitSymbol: 'box', ProductSubType: 'ProductMaster', ProductDimensionGroupName: 'Style' } },
          { id: 'A0002', name: 'Old toner', extra: { OnHold: 'Yes' } },
        ];
      if (c.path.startsWith('/items/A0001/dimensions'))
        return { itemNumber: 'A0001', storageDimensionGroup: 'None', mandatory: [], source: 'group',
          productDimensions: [{ dimension: 'Style', values: ['Classic', 'Premium'] }] };
      if (c.path.startsWith('/lookups')) return [];
      if (c.path === '/purchase-requisitions' && c.method === 'POST') return detail;
      if (c.path.startsWith('/purchase-requisitions/')) return detail;
      return null;
    });

    await userEvent.click(screen.getByRole('button', { name: 'Create requisition' }));
    expect(await screen.findByText('Name is required.')).toBeInTheDocument();
    expect(screen.getByText('Select an item.')).toBeInTheDocument();
    expect(calls.some((c) => c.method === 'POST')).toBe(false);

    await userEvent.type(screen.getByRole('textbox', { name: /^Name/ }), 'Office supplies');
    const item = within(screen.getByRole('group', { name: 'Line 1' })).getByRole('combobox', { name: /^Item\b(?! sales)/ });
    await userEvent.click(item);
    await userEvent.type(item, 'A0');
    expect(await screen.findByRole('option', { name: /A0002.*On hold/ }, { timeout: 3000 })).toHaveAttribute('aria-disabled', 'true');
    await userEvent.click(await screen.findByRole('option', { name: /A0001/ }, { timeout: 3000 }));

    // Product master using Style only → only a Style field (values assigned to the product), and it is required.
    const line = screen.getByRole('group', { name: 'Line 1' });
    const style = await within(line).findByRole('combobox', { name: /^Style/ });
    expect(within(line).queryByRole('combobox', { name: /Variant|^Color|^Size/ })).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'Create requisition' }));
    expect(await screen.findByText('Select the style.')).toBeInTheDocument();
    await userEvent.click(style);
    await userEvent.click(await screen.findByRole('option', { name: 'Classic' }, { timeout: 3000 }));

    await userEvent.click(screen.getByRole('button', { name: 'Create requisition' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'POST')).toBe(true));
    const post = calls.find((c) => c.method === 'POST')!;
    expect(post.body).toMatchObject({
      name: 'Office supplies', requester: '000123',
      lines: [{ itemNumber: 'A0001', description: 'Printer paper', unit: 'box', quantity: 1, style: 'Classic' }],
    });
    expect(await screen.findByRole('heading', { name: 'PR-000042' })).toBeInTheDocument();
  }, 20000);
});

describe('editing a draft requisition with item storage dimensions', () => {
  const draft: PrDetail = { ...detail, canEdit: true };

  it('offers Edit only for drafts', async () => {
    renderAt('/purchase-requisitions/PR-000042', () => draft);
    expect(await screen.findByRole('button', { name: 'Edit' })).toBeInTheDocument();
  });

  it('shows only mandatory dimensions, clears the others and saves with PUT', async () => {
    // D365 fills a procurement category on item lines; the edit must not send it back with the item.
    const saved: PrDetail = { ...draft, lines: draft.lines.map((l) => ({ ...l, procurementCategory: 'Office' })) };
    const calls = renderAt('/purchase-requisitions/PR-000042/edit', (c) => {
      if (c.path.startsWith('/items/A0001/dimensions'))
        return { itemNumber: 'A0001', storageDimensionGroup: 'SiteOnly', mandatory: ['Site'], source: 'group' };
      if (c.path.startsWith('/lookups')) return [];
      if (c.method === 'PUT') return saved;
      return saved;
    });
    const line = await screen.findByRole('group', { name: 'Line 1' });
    expect(await within(line).findByText(/Storage dimension group SiteOnly: Site mandatory/)).toBeInTheDocument();
    expect(within(line).getByRole('combobox', { name: /^Site/ })).toBeInTheDocument();
    expect(within(line).queryByRole('combobox', { name: /^Warehouse/ })).toBeNull();
    expect(screen.getByRole('combobox', { name: /^Purpose/ })).toBeDisabled();

    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true));
    const put = calls.find((c) => c.method === 'PUT')!;
    expect(put.path).toBe('/purchase-requisitions/PR-000042');
    expect(put.body).toMatchObject({ name: 'Office supplies', lines: [{ lineNumber: 1, itemNumber: 'A0001', procurementCategory: null, site: '1', warehouse: null }] });
  }, 20000);

  it('blocks saving when a mandatory dimension is empty', async () => {
    const calls = renderAt('/purchase-requisitions/PR-000042/edit', (c) => {
      if (c.path.startsWith('/items/'))
        return { itemNumber: 'A0001', storageDimensionGroup: 'SiteWHLoc', mandatory: ['Site', 'Warehouse', 'Location'], source: 'group' };
      if (c.path.startsWith('/lookups')) return [];
      return { ...draft, lines: [{ ...draft.lines[0], warehouse: null }] };
    });
    const line = await screen.findByRole('group', { name: 'Line 1' });
    expect(await within(line).findByRole('combobox', { name: /^Warehouse/ })).toBeInTheDocument();
    // Requisition lines have no location field, so Location is not asked for.
    expect(within(line).queryByRole('combobox', { name: /^Location/ })).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }));
    expect(await screen.findByText('Warehouse is mandatory for this item.')).toBeInTheDocument();
    expect(calls.some((c) => c.method === 'PUT')).toBe(false);
  }, 20000);
});
