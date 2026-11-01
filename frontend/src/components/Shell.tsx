import { useEffect, useState } from 'react';
import { makeStyles, tokens } from '@fluentui/react-components';
import { Navigate, Route, Routes } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { TopBar } from './TopBar';
import { LeftNav } from './LeftNav';
import { HomePage } from '@/pages/HomePage';
import { PlaceholderPage } from '@/pages/PlaceholderPage';
import { AdminConnectionPage, AdminUsersPage } from '@/pages/admin/AdminPages';
import { TenantDetailPage } from '@/pages/platform/TenantDetailPage';
import { TenantsPage } from '@/pages/platform/TenantsPage';
import { visibleNav } from '@/navigation';
import { useCurrentMembership, useSessionStore } from '@/state/sessionStore';

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
  const company = membership?.currentCompany ?? membership?.defaultCompany ?? null;

  return (
    <div className={s.root}>
      <TopBar
        userName={userName}
        email={me.email}
        tenantName={membership?.tenantName ?? null}
        company={company}
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
            {viewer.role === 'TenantAdmin' && (
              <>
                <Route path="/admin/users" element={<AdminUsersPage />} />
                <Route path="/admin/connection" element={<AdminConnectionPage />} />
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
