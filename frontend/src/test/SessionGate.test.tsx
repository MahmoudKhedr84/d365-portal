import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { ApiProvider } from '@/api/ApiProvider';
import { ApiError, type ApiRequest } from '@/api/client';
import type { Me } from '@/api/session';
import { SessionGate } from '@/components/SessionGate';
import { useSessionStore } from '@/state/sessionStore';
import { makeMe } from './fixtures';

function renderGate(api: ApiRequest, onSignOut = vi.fn()) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <FluentProvider theme={webLightTheme}>
      <QueryClientProvider client={qc}>
        <ApiProvider baseUrl="/api" client={api}>
          <MemoryRouter>
            <SessionGate onSignOut={onSignOut} />
          </MemoryRouter>
        </ApiProvider>
      </QueryClientProvider>
    </FluentProvider>,
  );
  return { onSignOut };
}

const apiReturning = (me: Me): ApiRequest => (async () => me) as unknown as ApiRequest;

describe('SessionGate', () => {
  beforeEach(() => useSessionStore.getState().reset());

  it('shows Not authorized for an unprovisioned account and lets the user sign out', async () => {
    const { onSignOut } = renderGate(apiReturning(makeMe([])));
    expect(await screen.findByRole('heading', { name: 'Not authorized' })).toBeInTheDocument();
    expect(screen.getByText(/alice@contoso.com is not set up/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /sign out/i }));
    expect(onSignOut).toHaveBeenCalled();
  });

  it('opens the only tenant directly with its default company', async () => {
    renderGate(apiReturning(makeMe([{ tenantName: 'Contoso', defaultCompany: 'USMF' }])));
    expect(await screen.findByRole('heading', { name: 'Welcome, Alice' })).toBeInTheDocument();
    expect(screen.getByText('Contoso · company USMF')).toBeInTheDocument();
    expect(screen.getAllByRole('link', { name: /purchase orders/i }).length).toBeGreaterThan(0);
    expect(screen.queryByRole('link', { name: /users/i })).not.toBeInTheDocument();
  });

  it('asks which tenant to open when the account is in several, then opens it', async () => {
    renderGate(apiReturning(makeMe([{ tenantName: 'Contoso' }, { tenantName: 'Fabrikam', role: 'TenantAdmin', defaultCompany: 'DEMF', companies: ['DEMF'] }])));
    expect(await screen.findByRole('heading', { name: 'Choose an organization' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Open Fabrikam' }));
    expect(await screen.findByText('Fabrikam · company DEMF')).toBeInTheDocument();
    // Tenant admin sees the administration area.
    expect(screen.getAllByRole('link', { name: /users/i }).length).toBeGreaterThan(0);
  });

  it('sends a super-admin without tenants to the platform area', async () => {
    renderGate(apiReturning(makeMe([], true)));
    expect(await screen.findByRole('heading', { name: 'Welcome, Alice' })).toBeInTheDocument();
    expect(screen.getAllByRole('link', { name: /tenants/i }).length).toBeGreaterThan(0);
    expect(screen.queryByRole('link', { name: /purchase orders/i })).not.toBeInTheDocument();
  });

  it('shows the server error with a retry button when the session call fails', async () => {
    const api = (async () => {
      throw new ApiError(500, 'server_error', 'Something went wrong. Reference: abc');
    }) as unknown as ApiRequest;
    renderGate(api);
    // 5xx is retried twice with backoff before the error is shown.
    expect(await screen.findByText(/Something went wrong/, {}, { timeout: 8000 })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
  }, 10000);
});
