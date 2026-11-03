import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { slugify, userFormSchema, connectionFormSchema, type AdminScope, type Connection, type User } from '@/api/admin';
import { ApiError } from '@/api/client';
import { ConnectionPanel } from '@/components/admin/ConnectionPanel';
import { UsersPanel } from '@/components/admin/UsersPanel';
import { AppToaster } from '@/components/notify';
import { parseIdList } from '@/pages/platform/TenantsPage';

function wrap(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <FluentProvider theme={webLightTheme}>
      <QueryClientProvider client={qc}>
        <AppToaster />
        {ui}
      </QueryClientProvider>
    </FluentProvider>,
  );
}

const user = (over: Partial<User> = {}): User => ({
  id: '11111111-1111-4111-8111-111111111111',
  email: 'alice@contoso.com',
  displayName: 'Alice',
  role: 'TenantAdmin',
  defaultCompany: 'USMF',
  companies: ['USMF'],
  isActive: true,
  isSignedInOnce: true,
  createdAt: '2026-11-01T00:00:00Z',
  lastLoginAt: null,
  ...over,
});

function fakeScope(over: Partial<AdminScope> = {}): AdminScope {
  return {
    key: ['test', Math.random()],
    getConnection: vi.fn(async () => null),
    saveConnection: vi.fn(),
    testConnection: vi.fn(),
    getCompanies: vi.fn(async () => [{ id: 'DEMF', name: 'Germany' }, { id: 'USMF', name: 'USA' }]),
    checkEntities: vi.fn(async () => []),
    findEntitySets: vi.fn(async () => []),
    listUsers: vi.fn(async () => [user()]),
    createUser: vi.fn(async (f) => user({ id: '22222222-2222-4222-8222-222222222222', email: f.email, displayName: f.displayName || f.email })),
    updateUser: vi.fn(async (_id, p) => user(p as Partial<User>)),
    ...over,
  };
}

describe('form schemas', () => {
  it('slugify and id list parsing', () => {
    expect(slugify('YELO Holding Sandbox!')).toBe('yelo-holding-sandbox');
    expect(parseIdList('a, b\nc;  d')).toEqual(['a', 'b', 'c', 'd']);
  });

  it('default company must be among the allowed companies', () => {
    const r = userFormSchema.safeParse({ email: 'a@b.com', role: 'User', companies: ['USMF'], defaultCompany: 'DEMF', isActive: true });
    expect(r.success).toBe(false);
  });

  it('connection URL must be a dynamics.com environment', () => {
    const base = { entraTenantId: '42f614c3-4fac-437c-b5dd-b5526cb26925', clientId: 'b9475d47-58bc-4204-85aa-948e7a4bff69', isActive: true };
    expect(connectionFormSchema.safeParse({ ...base, environmentUrl: 'https://yelo-uat.sandbox.operations.dynamics.com' }).success).toBe(true);
    expect(connectionFormSchema.safeParse({ ...base, environmentUrl: 'https://yelosandbox.sandbox.operations.uae.dynamics.com/' }).success).toBe(true);
    expect(connectionFormSchema.safeParse({ ...base, environmentUrl: 'https://evil.example.com' }).success).toBe(false);
  });
});

describe('UsersPanel', () => {
  it('lists users and adds one with companies loaded from D365', async () => {
    const scope = fakeScope();
    wrap(<UsersPanel scope={scope} />);

    expect(await screen.findByText('alice@contoso.com')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Add user' }));

    // jsdom quirk: tabster's modalizer can leave the surface flagged hidden for the a11y-tree query.
    const dialog = await screen.findByRole('dialog', { hidden: true }, { timeout: 3000 });
    await userEvent.type(within(dialog).getByLabelText(/Microsoft sign-in email/), 'bob@contoso.com');
    await userEvent.click(await within(dialog).findByRole('checkbox', { name: /DEMF – Germany/, hidden: true }));
    await userEvent.click(within(dialog).getByRole('button', { name: 'Add user', hidden: true }));

    await waitFor(() => expect(scope.createUser).toHaveBeenCalled());
    const form = (scope.createUser as ReturnType<typeof vi.fn>).mock.calls[0]![0];
    expect(form).toMatchObject({ email: 'bob@contoso.com', companies: ['DEMF'], defaultCompany: 'DEMF', role: 'User' });
  });

  it('lets you type company codes when D365 is not connected yet', async () => {
    const scope = fakeScope({
      getCompanies: vi.fn(async () => {
        throw new ApiError(409, 'no_connection', 'Set up the D365 connection to load companies.');
      }),
    });
    wrap(<UsersPanel scope={scope} />);
    await userEvent.click(await screen.findByRole('button', { name: 'Add user' }));
    const dialog = await screen.findByRole('dialog');
    expect(await within(dialog).findByText(/Set up the D365 connection/)).toBeInTheDocument();

    await userEvent.type(within(dialog).getByPlaceholderText('e.g. USMF'), 'yhq{Enter}');
    expect(within(dialog).getByRole('checkbox', { name: /YHQ/ })).toBeChecked();
  });

  it('shows the server error inside the dialog', async () => {
    const scope = fakeScope({
      createUser: vi.fn(async () => {
        throw new ApiError(409, 'user_exists', 'bob@contoso.com is already a user of this tenant.');
      }),
    });
    wrap(<UsersPanel scope={scope} />);
    await userEvent.click(await screen.findByRole('button', { name: 'Add user' }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.type(within(dialog).getByLabelText(/Microsoft sign-in email/), 'bob@contoso.com');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Add user' }));
    expect(await within(dialog).findByText(/already a user of this tenant/)).toBeInTheDocument();
  });
});

describe('ConnectionPanel', () => {
  const saved: Connection = {
    useExtendedProductDimensions: false,
    environmentUrl: 'https://yelo-uat.sandbox.operations.dynamics.com',
    entraTenantId: '42f614c3-4fac-437c-b5dd-b5526cb26925',
    clientId: 'b9475d47-58bc-4204-85aa-948e7a4bff69',
    hasClientSecret: false,
    apiVersion: null,
    isActive: true,
    lastTestedAt: null,
    lastTestSucceeded: null,
    lastTestResult: null,
    updatedAt: '2026-11-01T00:00:00Z',
  };

  it('requires the secret when none is stored, then saves', async () => {
    const scope = fakeScope({
      getConnection: vi.fn(async () => saved),
      saveConnection: vi.fn(async () => ({ ...saved, hasClientSecret: true })),
    });
    wrap(<ConnectionPanel scope={scope} />);

    expect(await screen.findByDisplayValue(saved.environmentUrl)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText(/Client secret is required/)).toBeInTheDocument();
    expect(scope.saveConnection).not.toHaveBeenCalled();

    await userEvent.type(screen.getByLabelText(/Client secret/), 'typed-by-admin');
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(scope.saveConnection).toHaveBeenCalledWith(expect.objectContaining({ clientSecret: 'typed-by-admin' })));
  });

  it('shows the D365 error message when the test fails', async () => {
    const scope = fakeScope({
      getConnection: vi.fn(async () => ({ ...saved, hasClientSecret: true })),
      testConnection: vi.fn(async () => ({
        success: false,
        message: 'D365 rejected the token. Register the app in Microsoft Entra ID applications.',
        durationMs: 120,
        testedAt: '2026-11-01T00:00:00Z',
      })),
    });
    wrap(<ConnectionPanel scope={scope} />);
    await userEvent.click(await screen.findByRole('button', { name: 'Test connection' }));
    expect(await screen.findByText('Connection failed')).toBeInTheDocument();
    expect(screen.getByText(/Microsoft Entra ID applications\./)).toBeInTheDocument();
  });
});
