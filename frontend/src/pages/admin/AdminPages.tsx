import { useMemo } from 'react';
import { Body1, Title2, makeStyles } from '@fluentui/react-components';
import { tenantAdminScope } from '@/api/admin';
import { useApi } from '@/api/ApiProvider';
import { AuditLogPanel } from '@/components/admin/AuditLogPanel';
import { ConnectionPanel } from '@/components/admin/ConnectionPanel';
import { UsersPanel } from '@/components/admin/UsersPanel';
import { useCurrentMembership } from '@/state/sessionStore';

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', rowGap: '16px' },
});

function useTenantScope() {
  const api = useApi();
  const membership = useCurrentMembership();
  const tenantId = membership?.tenantId ?? 'none';
  return { scope: useMemo(() => tenantAdminScope(api, tenantId), [api, tenantId]), membership };
}

export function AdminUsersPage() {
  const s = useStyles();
  const { scope, membership } = useTenantScope();
  return (
    <div className={s.page}>
      <div>
        <Title2 as="h1">Users</Title2>
        <Body1>People who can use the portal in {membership?.tenantName}, their role and the D365 companies they may work in.</Body1>
      </div>
      <UsersPanel scope={scope} />
    </div>
  );
}

export function AdminConnectionPage() {
  const s = useStyles();
  const { scope } = useTenantScope();
  return (
    <div className={s.page}>
      <Title2 as="h1">D365 connection</Title2>
      <ConnectionPanel scope={scope} />
    </div>
  );
}

export function AdminAuditPage() {
  const s = useStyles();
  const { scope } = useTenantScope();
  return (
    <div className={s.page}>
      <Title2 as="h1">Audit log</Title2>
      <AuditLogPanel base="/admin" tenantScoped queryKey={scope.key} />
    </div>
  );
}
