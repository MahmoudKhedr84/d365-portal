import { useEffect, useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { makeStyles, tokens } from '@fluentui/react-components';
import { Navigate, Route, Routes } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { TopBar } from './TopBar';
import { LeftNav } from './LeftNav';
import { HomePage } from '@/pages/HomePage';
import { PlaceholderPage } from '@/pages/PlaceholderPage';
import { AdminAuditPage, AdminConnectionPage, AdminUsersPage } from '@/pages/admin/AdminPages';
import { CaseCreatePage, CaseDetailPage, CasesPage } from '@/pages/cases/CasesPages';
import { JournalCreatePage, JournalDetailPage, JournalsPage } from '@/pages/journals/JournalPages';
import { TenantDetailPage } from '@/pages/platform/TenantDetailPage';
import { TenantsPage } from '@/pages/platform/TenantsPage';
import { PurchaseOrdersPage } from '@/pages/purchaseOrders/PurchaseOrdersPage';
import { PurchaseOrderCreatePage } from '@/pages/purchaseOrders/PurchaseOrderCreatePage';
import { PurchaseOrderDetailPage } from '@/pages/purchaseOrders/PurchaseOrderDetailPage';
import { PurchaseRequisitionsPage } from '@/pages/purchaseRequisitions/PurchaseRequisitionsPage';
import { PurchaseRequisitionCreatePage } from '@/pages/purchaseRequisitions/PurchaseRequisitionCreatePage';
import { PurchaseRequisitionDetailPage } from '@/pages/purchaseRequisitions/PurchaseRequisitionDetailPage';
import { visibleNav } from '@/navigation';
import { useCurrentMembership, useSessionStore } from '@/state/sessionStore';
import { useApi } from '@/api/ApiProvider';
import { switchCompany } from '@/api/session';
import { useNotify } from './notify';

const useStyles = makeStyles({
  root: { height: '100vh', display: 'flex', flexDirection: 'column' },
  body: { flexGrow: 1, display: 'flex', minHeight: 0 },
  main: {
    flexGrow: 1,
    minWidth: 0,
    overflowY: 'auto',
    padding: '24px 16px',
    backgroundColor: tokens.colorNeutralBackground2,
    '@media (min-width: 768px)': { padding: '24px 32px' },
  },
});

const NARROW = '(max-width: 768px)';

export function Shell({ onSignOut }: { onSignOut: () => void }) {
  const { t } = useTranslation();
  const s = useStyles();
  const me = useSessionStore((st) => st.me)!;
  const clearTenant = useSessionStore((st) => st.clearTenant);
  const membership = useCurrentMembership();
  const company = useSessionStore((st) => st.company);
  const selectCompany = useSessionStore((st) => st.selectCompany);
  const api = useApi();
  const qc = useQueryClient();
  const notify = useNotify();

  // Switch immediately (every request carries X-Company), persist as the user's current company,
  // and refetch company-dependent data. Revert if the server refuses.
  const companySwitch = useMutation({
    mutationFn: (next: string) => switchCompany(api, next),
    onMutate: (next) => {
      const previous = company;
      selectCompany(next);
      void qc.invalidateQueries({ predicate: (q) => q.queryKey[0] !== 'session' });
      return { previous };
    },
    onSuccess: (_, next) => notify.success(t('topBar.companySwitched', { company: next })),
    onError: (e, _next, ctx) => {
      if (ctx?.previous) selectCompany(ctx.previous);
      void qc.invalidateQueries({ predicate: (q) => q.queryKey[0] !== 'session' });
      notify.error('Could not switch company', e);
    },
  });

  const [collapsed, setCollapsed] = useState(() => window.matchMedia?.(NARROW).matches ?? false);
  useEffect(() => {
    const mq = window.matchMedia?.(NARROW);
    if (!mq) return;
    const onChange = (e: MediaQueryListEvent) => setCollapsed(e.matches);
    mq.addEventListener('change', onChange);
    return () => mq.removeEventListener('change', onChange);
  }, []);

  const viewer = { role: membership?.role ?? null, isSuperAdmin: me.isSuperAdmin };
  const items = visibleNav(viewer);
  const userName = me.displayName ?? me.email ?? 'User';

  return (
    <div className={s.root}>
      <TopBar
        userName={userName}
        email={me.email}
        tenantName={membership?.tenantName ?? null}
        company={company}
        companies={membership?.companies ?? []}
        onSwitchCompany={(c) => companySwitch.mutate(c)}
        switchingCompany={companySwitch.isPending}
        canSwitchTenant={me.memberships.length > 1}
        onSwitchTenant={clearTenant}
        onSignOut={onSignOut}
        onToggleNav={() => setCollapsed((c) => !c)}
      />
      <div className={s.body}>
        <LeftNav items={items} collapsed={collapsed} />
        <main className={s.main}>
          <Routes>
            <Route
              path="/"
              element={
                <HomePage
                  userName={userName}
                  tenantName={membership?.tenantName ?? null}
                  company={company}
                  tiles={items.filter((i) => i.path !== '/')}
                />
              }
            />
            {viewer.role && (
              <>
                <Route path="/purchase-requisitions" element={<PurchaseRequisitionsPage />} />
                <Route path="/purchase-requisitions/new" element={<PurchaseRequisitionCreatePage />} />
                <Route path="/purchase-requisitions/:number" element={<PurchaseRequisitionDetailPage />} />
                <Route path="/purchase-orders" element={<PurchaseOrdersPage />} />
                <Route path="/purchase-orders/new" element={<PurchaseOrderCreatePage />} />
                <Route path="/purchase-orders/:number" element={<PurchaseOrderDetailPage />} />
                <Route path="/cases" element={<CasesPage />} />
                <Route path="/cases/new" element={<CaseCreatePage />} />
                <Route path="/cases/:caseId" element={<CaseDetailPage />} />
                <Route path="/general-journals" element={<JournalsPage />} />
                <Route path="/general-journals/new" element={<JournalCreatePage />} />
                <Route path="/general-journals/:number" element={<JournalDetailPage />} />
              </>
            )}
            {viewer.role === 'TenantAdmin' && (
              <>
                <Route path="/admin/users" element={<AdminUsersPage />} />
                <Route path="/admin/connection" element={<AdminConnectionPage />} />
                <Route path="/admin/audit" element={<AdminAuditPage />} />
              </>
            )}
            {me.isSuperAdmin && (
              <>
                <Route path="/platform/tenants" element={<TenantsPage />} />
                <Route path="/platform/tenants/:tenantId" element={<TenantDetailPage />} />
              </>
            )}
            {items
              .filter((i) => i.step)
              .map((i) => (
                <Route key={i.path} path={`${i.path}/*`} element={<PlaceholderPage title={t(i.labelKey)} step={i.step!} />} />
              ))}
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </main>
      </div>
    </div>
  );
}
