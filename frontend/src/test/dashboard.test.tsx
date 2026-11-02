import { describe, expect, it, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { ApiProvider } from '@/api/ApiProvider';
import type { ApiRequest } from '@/api/client';
import type { Dashboard as DashboardData } from '@/api/dashboard';
import { Dashboard } from '@/components/dashboard/Dashboard';
import { useSessionStore } from '@/state/sessionStore';
import { makeMe } from './fixtures';

const days = Array.from({ length: 14 }, (_, i) => ({ date: `2026-10-${String(20 + i).padStart(2, '0')}`, count: i === 13 ? 3 : i === 12 ? 1 : 0 }));
const data: DashboardData = {
  company: 'WFQ',
  kpis: [
    { key: 'poOpen', label: 'Open purchase orders', value: 12, link: '/purchase-orders', error: null },
    { key: 'casesOverdue', label: 'Overdue CAPA cases', value: 2, link: '/cases', error: null },
    { key: 'casesOpen', label: 'Open CAPA cases', value: null, link: '/cases', error: 'Entity not found' },
  ],
  createdPerDay: days,
  createdByType: { PurchaseOrder: 3, GeneralJournal: 1 },
  failedD365CallsLast7Days: 4,
  recentActivity: [{ timestamp: '2026-11-02T10:00:00Z', userEmail: 'u@contoso.com', company: 'WFQ', entity: 'PurchaseOrder', summary: 'WFQ-PO-9: vendor V1; 1 line(s)' }],
  recentPurchaseOrders: [{ purchaseOrderNumber: 'WFQ-PO-9', vendorName: 'Fabrikam', status: 'Backorder', approvalStatus: 'Draft', requestedDeliveryDate: null }],
  error: null,
};

beforeEach(() => {
  useSessionStore.getState().reset();
  useSessionStore.getState().setMe(makeMe([{ companies: ['WFQ'], defaultCompany: 'WFQ' }]));
});

describe('home dashboard', () => {
  it('shows KPIs, the 14-day chart, breakdown, recent POs and activity', async () => {
    const api = (async () => data) as unknown as ApiRequest;
    render(
      <FluentProvider theme={webLightTheme}>
        <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
          <ApiProvider baseUrl="/api" client={api}>
            <MemoryRouter><Dashboard isAdmin /></MemoryRouter>
          </ApiProvider>
        </QueryClientProvider>
      </FluentProvider>,
    );
    expect(await screen.findByRole('link', { name: 'Open purchase orders: 12' })).toHaveAttribute('href', '/purchase-orders');
    expect(screen.getByRole('link', { name: 'Open CAPA cases: unavailable' })).toBeInTheDocument();
    expect(screen.getByText('in the last 14 days')).toBeInTheDocument();
    const bars = screen.getAllByRole('listitem').filter((el) => el.getAttribute('aria-label')?.includes(':'));
    fireEvent.mouseEnter(bars[13]!);
    expect(screen.getByText(/created on/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Purchase orders' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '4' })).toHaveAttribute('href', '/admin/audit');
    expect(screen.getByRole('link', { name: 'WFQ-PO-9' })).toBeInTheDocument();
    expect(screen.getByText(/WFQ-PO-9: vendor V1/)).toBeInTheDocument();
  });
});
