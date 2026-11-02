import { describe, expect, it, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { ApiProvider } from '@/api/ApiProvider';
import type { ApiRequest } from '@/api/client';
import { accountDisplay, emptyJournalLine, journalTotals, toJournalBody, validateJournalLine } from '@/api/journals';
import { AuditLogPanel } from '@/components/admin/AuditLogPanel';
import { AppToaster } from '@/components/notify';
import { CaseCreatePage, CaseDetailPage } from '@/pages/cases/CasesPages';
import { JournalCreatePage, JournalDetailPage } from '@/pages/journals/JournalPages';
import { useSessionStore } from '@/state/sessionStore';
import { makeMe } from './fixtures';

type Call = { path: string; method?: string; body?: unknown };

function renderWith(path: string, routes: ReactNode, handler: (c: Call) => unknown) {
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
          <MemoryRouter initialEntries={[path]}><Routes>{routes}</Routes></MemoryRouter>
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

const caseDetail = {
  caseId: 'CAPA-0042', company: 'WFQ', description: 'Leaking valve', name: null, status: 'Planned', classification: 'Corrective',
  category: 'QA', subCategory: null, type: null, source: null, priority: 'High', dueDate: '2026-11-30', plannedEffectiveDate: null,
  responsible: '015115', responsibleName: 'Admin', notes: 'Check gasket', createdAt: '2026-11-02T10:00:00Z',
  d365Url: 'https://x.operations.dynamics.com/?cmp=wfq&mi=QMSCAPACaseListPage',
};

describe('CAPA cases', () => {
  it('validates and creates a case', async () => {
    const calls = renderWith('/cases/new', <>
      <Route path="/cases/new" element={<CaseCreatePage />} />
      <Route path="/cases/:caseId" element={<CaseDetailPage />} />
    </>, (c) => (c.path.startsWith('/lookups') ? [] : caseDetail));

    await userEvent.click(screen.getByRole('button', { name: 'Create case' }));
    expect(await screen.findByText('Description is required.')).toBeInTheDocument();
    await userEvent.type(screen.getByRole('textbox', { name: /^Description/ }), 'Leaking valve');
    await userEvent.type(screen.getByRole('textbox', { name: /^Priority/ }), 'High');
    await userEvent.click(screen.getByRole('button', { name: 'Create case' }));

    await waitFor(() => expect(calls.some((c) => c.method === 'POST')).toBe(true));
    expect(calls.find((c) => c.method === 'POST')!.body).toMatchObject({ description: 'Leaking valve', classification: 'Corrective', priority: 'High', category: null });
    expect(await screen.findByRole('heading', { name: 'CAPA-0042' })).toBeInTheDocument();
    expect(screen.getByText('Check gasket')).toBeInTheDocument();
  }, 20000);
});

describe('general journal helpers', () => {
  const base = { ...emptyJournalLine(), date: '2026-11-02', currency: 'EGP' };

  it('builds ledger account display values with dimensions', () => {
    expect(accountDisplay({ accountType: 'Ledger', account: '110110', dimensions: '001-022' })).toBe('110110-001-022');
    expect(accountDisplay({ accountType: 'Ledger', account: '110110', dimensions: '-001' })).toBe('110110-001');
    expect(accountDisplay({ accountType: 'Vend', account: 'V1', dimensions: '001' })).toBe('V1');
  });

  it('requires exactly one of debit or credit', () => {
    expect(validateJournalLine({ ...base, account: 'A', debit: '10' })).toEqual({});
    expect(validateJournalLine({ ...base, account: 'A', debit: '10', credit: '5' }).amount).toBeDefined();
    expect(validateJournalLine({ ...base, account: 'A' }).amount).toBeDefined();
    expect(validateJournalLine({ ...base, account: 'A', debit: '1', offsetAccount: 'B' }).offsetAccountType).toBeDefined();
  });

  it('keeps running totals per currency and counts offset lines on both sides', () => {
    const lines = [
      { ...base, account: '1', debit: '100' },
      { ...base, account: '2', credit: '60' },
      { ...base, account: '3', credit: '25', offsetAccountType: 'Bank' as const, offsetAccount: 'BNK' },
      { ...base, account: '4', debit: '5', currency: 'usd' },
    ];
    expect(journalTotals(lines)).toEqual([
      { currency: 'EGP', debit: 125, credit: 85, balance: 40 },
      { currency: 'USD', debit: 5, credit: 0, balance: 5 },
    ]);
    const body = toJournalBody('GenJrn', '', lines);
    expect(body.description).toBeNull();
    expect(body.lines[2]).toMatchObject({ credit: 25, debit: null, offsetAccountType: 'Bank', offsetAccount: 'BNK', currency: 'EGP' });
  });
});

describe('general journal pages', () => {
  it('shows running totals while entering lines', async () => {
    renderWith('/general-journals/new', <Route path="/general-journals/new" element={<JournalCreatePage />} />, () => []);
    const line1 = screen.getByRole('group', { name: 'Line 1' });
    const line2 = screen.getByRole('group', { name: 'Line 2' });
    await userEvent.type(within(line1).getByRole('spinbutton', { name: 'Debit' }), '100');
    await userEvent.type(within(line2).getByRole('spinbutton', { name: 'Credit' }), '40');
    expect(screen.getByLabelText('Running totals')).toHaveTextContent('Out of balance 60.00');
    await userEvent.clear(within(line2).getByRole('spinbutton', { name: 'Credit' }));
    await userEvent.type(within(line2).getByRole('spinbutton', { name: 'Credit' }), '100');
    expect(screen.getByLabelText('Running totals')).toHaveTextContent('Balanced');

    await userEvent.click(screen.getByRole('button', { name: 'Create journal' }));
    expect(await screen.findByText('Select a journal name.')).toBeInTheDocument();
    expect(screen.getAllByText('Account is required.').length).toBe(2);
  });

  it('shows journal lines and totals', async () => {
    renderWith('/general-journals/GJ-0099', <Route path="/general-journals/:number" element={<JournalDetailPage />} />, () => ({
      journalBatchNumber: 'GJ-0099', company: 'WFQ', journalName: 'GenJrn', description: 'Nov', posted: false,
      lines: [{ lineNumber: 1, date: '2026-11-02', voucher: 'V-1', accountType: 'Ledger', account: '110110', text: 'x', debit: 100, credit: 0,
        currency: 'EGP', offsetAccountType: null, offsetAccount: null }],
      totals: [{ currency: 'EGP', debit: 100, credit: 0, balance: 100 }],
      d365Url: 'https://x.operations.dynamics.com/?cmp=wfq&mi=LedgerJournalTable5',
    }));
    expect(await screen.findByRole('heading', { name: 'GJ-0099' })).toBeInTheDocument();
    expect(screen.getByText('Open')).toBeInTheDocument();
    expect(screen.getByLabelText('Totals')).toHaveTextContent('Balance 100.00');
  });
});

describe('audit log', () => {
  it('lists entries, filters failures and expands details', async () => {
    const calls = renderWith('/', <Route path="/" element={<AuditLogPanel base="/admin" tenantScoped queryKey={['t']} />} />, () => ({
      items: [{ id: 7, timestamp: '2026-11-02T10:00:00Z', tenantId: null, userEmail: 'u@contoso.com', company: 'WFQ', entity: 'PurchaseRequisitionLines',
        action: 'D365.BATCH', requestSummary: '$batch: POST PurchaseRequisitionLines', responseSummary: 'Style Acdelco has not been assigned',
        statusCode: 400, success: false, durationMs: 812, correlationId: 'abc' }],
      total: 1,
    }));
    expect(await screen.findByText('D365.BATCH')).toBeInTheDocument();
    expect(screen.getByText('Failed 400')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Show details' }));
    expect(screen.getByText(/Style Acdelco has not been assigned/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('combobox', { name: 'Result' }));
    await userEvent.click(await screen.findByRole('option', { name: 'Failures only' }));
    await waitFor(() => expect(calls.some((c) => c.path.startsWith('/admin/audit?') && c.path.includes('success=false'))).toBe(true));
  });
});
