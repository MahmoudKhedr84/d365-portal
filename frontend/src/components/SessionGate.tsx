import { useEffect } from 'react';
import { Button, MessageBar, MessageBarActions, MessageBarBody, MessageBarTitle, Spinner, makeStyles } from '@fluentui/react-components';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useApi } from '@/api/ApiProvider';
import { ApiError } from '@/api/client';
import { startSession } from '@/api/session';
import { useSessionStore } from '@/state/sessionStore';
import { NotAuthorizedPage } from '@/pages/NotAuthorizedPage';
import { TenantPickerPage } from '@/pages/TenantPickerPage';
import { CenteredPanel } from './CenteredPanel';
import { Shell } from './Shell';

const useStyles = makeStyles({
  center: { minHeight: '100vh', display: 'grid', placeItems: 'center' },
});

/**
 * After Microsoft sign-in: resolves the account to its tenants and decides what to show —
 * Not authorized, the tenant picker, or the app shell.
 */
export function SessionGate({ onSignOut }: { onSignOut: () => void }) {
  const { t } = useTranslation();
  const s = useStyles();
  const api = useApi();
  const { me, tenantId, setMe, selectTenant } = useSessionStore();

  const session = useQuery({
    queryKey: ['session'],
    queryFn: () => startSession(api),
    staleTime: Infinity,
    retry: (count, error) => !(error instanceof ApiError && error.status > 0 && error.status < 500) && count < 2,
  });

  useEffect(() => {
    if (session.data) setMe(session.data);
  }, [session.data, setMe]);

  if (session.isError) {
    const err = session.error;
    return (
      <CenteredPanel title={t('session.failed')}>
        <MessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>{err instanceof ApiError ? err.code : 'error'}</MessageBarTitle>
            {err.message}
          </MessageBarBody>
          <MessageBarActions>
            <Button onClick={() => session.refetch()}>{t('app.retry')}</Button>
          </MessageBarActions>
        </MessageBar>
      </CenteredPanel>
    );
  }

  if (!me) {
    return (
      <div className={s.center}>
        <Spinner label={t('app.loading')} />
      </div>
    );
  }

  if (me.memberships.length === 0 && !me.isSuperAdmin) {
    return <NotAuthorizedPage email={me.email} onSignOut={onSignOut} />;
  }

  // Super-admins with no tenant membership go straight to the platform area.
  if (!tenantId && me.memberships.length > 0) {
    return <TenantPickerPage memberships={me.memberships} onSelect={selectTenant} />;
  }

  return <Shell onSignOut={onSignOut} />;
}
