import { useEffect, useMemo, useState } from 'react';
import {
  Body1,
  Breadcrumb,
  BreadcrumbButton,
  BreadcrumbDivider,
  BreadcrumbItem,
  Button,
  Caption1,
  Card,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Field,
  Input,
  MessageBar,
  MessageBarBody,
  Spinner,
  Subtitle2,
  Switch,
  Textarea,
  Title2,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { Delete20Regular, Save20Regular } from '@fluentui/react-icons';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate, useParams } from 'react-router-dom';
import { deleteTenant, getTenant, platformTenantScope, updateTenant, type Tenant } from '@/api/admin';
import { useApi } from '@/api/ApiProvider';
import { ConnectionPanel } from '@/components/admin/ConnectionPanel';
import { UsersPanel } from '@/components/admin/UsersPanel';
import { errorMessage, useNotify } from '@/components/notify';
import { parseIdList } from './TenantsPage';

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', rowGap: '16px' },
  card: { padding: '20px', rowGap: '12px', maxWidth: '760px' },
  grid: {
    display: 'grid',
    gap: '12px 16px',
    gridTemplateColumns: '1fr',
    '@media (min-width: 720px)': { gridTemplateColumns: '1fr 1fr' },
  },
  full: { '@media (min-width: 720px)': { gridColumn: '1 / -1' } },
  muted: { color: tokens.colorNeutralForeground3 },
  mono: { fontFamily: tokens.fontFamilyMonospace },
  danger: {
    padding: '20px',
    rowGap: '12px',
    maxWidth: '760px',
    border: `1px solid ${tokens.colorPaletteRedBorder2}`,
  },
  dangerTitle: { color: tokens.colorPaletteRedForeground1 },
  deleteButton: {
    backgroundColor: tokens.colorPaletteRedBackground3,
    color: tokens.colorNeutralForegroundOnBrand,
    ':hover': { backgroundColor: tokens.colorPaletteRedForeground1, color: tokens.colorNeutralForegroundOnBrand },
    ':hover:active': { backgroundColor: tokens.colorPaletteRedForeground1, color: tokens.colorNeutralForegroundOnBrand },
  },
});

/** Permanent deletion with a typed-slug confirmation. */
export function DeleteTenantCard({ tenant }: { tenant: Tenant }) {
  const s = useStyles();
  const api = useApi();
  const qc = useQueryClient();
  const notify = useNotify();
  const navigate = useNavigate();
  const [open, setOpen] = useState(false);
  const [typed, setTyped] = useState('');

  const remove = useMutation({
    mutationFn: () => deleteTenant(api, tenant.id, typed.trim()),
    onSuccess: () => {
      setOpen(false);
      qc.removeQueries({ queryKey: ['platform', tenant.id] });
      void qc.invalidateQueries({ queryKey: ['platform', 'tenants'] });
      void qc.invalidateQueries({ queryKey: ['session'] }); // the signed-in user may have belonged to it
      notify.success('Tenant deleted', `${tenant.name} and its users were removed.`);
      navigate('/platform/tenants');
    },
    onError: (e) => notify.error('Could not delete the tenant', e),
  });

  return (
    <Card className={s.danger}>
      <Subtitle2 as="h2" className={s.dangerTitle}>Danger zone</Subtitle2>
      <Body1>
        Deleting <strong>{tenant.name}</strong> removes its D365 connection, all {tenant.userCount} user
        {tenant.userCount === 1 ? '' : 's'} and their company access. Audit history is kept. This can’t be undone.
      </Body1>
      <div>
        <Button className={s.deleteButton} icon={<Delete20Regular />} onClick={() => { setTyped(''); setOpen(true); }}>
          Delete tenant
        </Button>
      </div>
      <Dialog open={open} onOpenChange={(_, d) => setOpen(d.open)}>
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Delete {tenant.name}?</DialogTitle>
            <DialogContent>
              <Field label={<>Type <span className={s.mono}>{tenant.slug}</span> to confirm</>} required>
                <Input value={typed} onChange={(_, d) => setTyped(d.value)} autoComplete="off" aria-label="Tenant slug" />
              </Field>
            </DialogContent>
            <DialogActions>
              <Button onClick={() => setOpen(false)}>Cancel</Button>
              <Button className={s.deleteButton} icon={<Delete20Regular />}
                disabled={typed.trim() !== tenant.slug || remove.isPending}
                onClick={() => remove.mutate()}>
                {remove.isPending ? 'Deleting…' : 'Delete permanently'}
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </Card>
  );
}

export function TenantDetailPage() {
  const s = useStyles();
  const { tenantId = '' } = useParams();
  const api = useApi();
  const qc = useQueryClient();
  const notify = useNotify();
  const navigate = useNavigate();
  const scope = useMemo(() => platformTenantScope(api, tenantId), [api, tenantId]);

  const tenant = useQuery({ queryKey: ['platform', tenantId, 'tenant'], queryFn: () => getTenant(api, tenantId) });
  const [name, setName] = useState('');
  const [active, setActive] = useState(true);
  const [dirs, setDirs] = useState('');

  useEffect(() => {
    if (!tenant.data) return;
    setName(tenant.data.name);
    setActive(tenant.data.isActive);
    setDirs(tenant.data.allowedEntraTenantIds.join('\n'));
  }, [tenant.data]);

  const save = useMutation({
    mutationFn: () => updateTenant(api, tenantId, { name, isActive: active, allowedEntraTenantIds: parseIdList(dirs) }),
    onSuccess: (t) => {
      qc.setQueryData(['platform', tenantId, 'tenant'], t);
      qc.invalidateQueries({ queryKey: ['platform', 'tenants'] });
      notify.success('Tenant saved');
    },
    onError: (e) => notify.error('Could not save the tenant', e),
  });

  if (tenant.isLoading) return <Spinner label="Loading tenant…" />;
  if (tenant.isError)
    return <MessageBar intent="error"><MessageBarBody>{errorMessage(tenant.error)}</MessageBarBody></MessageBar>;
  const t = tenant.data!;

  return (
    <div className={s.page}>
      <Breadcrumb aria-label="Breadcrumb">
        <BreadcrumbItem><BreadcrumbButton onClick={() => navigate('/platform/tenants')}>Tenants</BreadcrumbButton></BreadcrumbItem>
        <BreadcrumbDivider />
        <BreadcrumbItem><BreadcrumbButton current>{t.name}</BreadcrumbButton></BreadcrumbItem>
      </Breadcrumb>
      <div>
        <Title2 as="h1">{t.name}</Title2>
        <Body1 className={s.muted}>Slug <span className={s.mono}>{t.slug}</span> · created {new Date(t.createdAt).toLocaleDateString()}</Body1>
      </div>

      <Card className={s.card}>
        <Subtitle2 as="h2">Organization</Subtitle2>
        <div className={s.grid}>
          <Field label="Name" required>
            <Input value={name} onChange={(_, d) => setName(d.value)} />
          </Field>
          <Field label="Status">
            <Switch checked={active} label={active ? 'Active' : 'Disabled — nobody in this tenant can sign in'} onChange={(_, d) => setActive(d.checked)} />
          </Field>
          <Field className={s.full} label="Allowed Microsoft Entra tenant IDs"
            hint="Only accounts from these directories can claim a user invited by email. One per line.">
            <Textarea value={dirs} onChange={(_, d) => setDirs(d.value)} resize="vertical" />
          </Field>
        </div>
        {parseIdList(dirs).length === 0 && (
          <Caption1 className={s.muted}>No directories allowed yet — invited users won’t be able to sign in.</Caption1>
        )}
        <div>
          <Button appearance="primary" icon={<Save20Regular />} onClick={() => save.mutate()} disabled={save.isPending || !name.trim()}>
            {save.isPending ? 'Saving…' : 'Save'}
          </Button>
        </div>
      </Card>

      <ConnectionPanel scope={scope} />
      <UsersPanel scope={scope} title="Users and tenant admins" />
      <DeleteTenantCard tenant={t} />
    </div>
  );
}
