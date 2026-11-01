import { useMemo, useState } from 'react';
import {
  Badge,
  Body1,
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
  Skeleton,
  SkeletonItem,
  Table,
  TableBody,
  TableCell,
  TableCellLayout,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Textarea,
  Title2,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { Add20Regular, Building20Regular, Search16Regular } from '@fluentui/react-icons';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { createTenant, listTenants, slugify, tenantFormSchema, type TenantForm } from '@/api/admin';
import { useApi } from '@/api/ApiProvider';
import { ConnectionStatusBadge } from '@/components/admin/ConnectionPanel';
import { errorMessage, fieldErrors, useNotify } from '@/components/notify';

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', rowGap: '16px' },
  toolbar: { display: 'flex', alignItems: 'center', columnGap: '12px', flexWrap: 'wrap' },
  spacer: { flexGrow: 1 },
  card: { padding: '8px 12px' },
  tableWrap: { overflowX: 'auto' },
  row: { cursor: 'pointer' },
  muted: { color: tokens.colorNeutralForeground3 },
  form: { display: 'flex', flexDirection: 'column', rowGap: '12px' },
});

export const parseIdList = (text: string) =>
  text
    .split(/[\s,;]+/)
    .map((x) => x.trim())
    .filter(Boolean);

export function TenantsPage() {
  const s = useStyles();
  const api = useApi();
  const qc = useQueryClient();
  const notify = useNotify();
  const navigate = useNavigate();
  const tenants = useQuery({ queryKey: ['platform', 'tenants'], queryFn: () => listTenants(api) });

  const [search, setSearch] = useState('');
  const [open, setOpen] = useState(false);
  const [name, setName] = useState('');
  const [slug, setSlug] = useState('');
  const [slugTouched, setSlugTouched] = useState(false);
  const [dirs, setDirs] = useState('');
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [serverError, setServerError] = useState<string | null>(null);

  const create = useMutation({
    mutationFn: (form: TenantForm) => createTenant(api, form),
    onSuccess: (t) => {
      qc.invalidateQueries({ queryKey: ['platform', 'tenants'] });
      setOpen(false);
      notify.success('Tenant created', 'Next: set the D365 connection and add the first tenant admin.');
      navigate(`/platform/tenants/${t.id}`);
    },
    onError: (e) => setServerError(errorMessage(e)),
  });

  const startCreate = () => {
    setName(''); setSlug(''); setSlugTouched(false); setDirs(''); setErrors({}); setServerError(null);
    setOpen(true);
  };

  const submit = () => {
    const parsed = tenantFormSchema.safeParse({ name, slug, allowedEntraTenantIds: parseIdList(dirs) });
    if (!parsed.success) return setErrors(fieldErrors(parsed.error.issues));
    setErrors({});
    create.mutate(parsed.data);
  };

  const rows = useMemo(() => {
    const q = search.trim().toLowerCase();
    return (tenants.data ?? []).filter((t) => !q || t.name.toLowerCase().includes(q) || t.slug.includes(q));
  }, [tenants.data, search]);

  return (
    <div className={s.page}>
      <div className={s.toolbar}>
        <div>
          <Title2 as="h1">Tenants</Title2>
          <Body1 className={s.muted}>Customer organizations, each with its own D365 environment and users.</Body1>
        </div>
        <span className={s.spacer} />
        <Input contentBefore={<Search16Regular />} placeholder="Search tenants" value={search}
          onChange={(_, d) => setSearch(d.value)} aria-label="Search tenants" />
        <Button appearance="primary" icon={<Add20Regular />} onClick={startCreate}>New tenant</Button>
      </div>

      <Card className={s.card}>
        {tenants.isLoading ? (
          <Skeleton aria-label="Loading tenants"><SkeletonItem size={32} /><SkeletonItem size={32} /></Skeleton>
        ) : tenants.isError ? (
          <MessageBar intent="error"><MessageBarBody>{errorMessage(tenants.error)}</MessageBarBody></MessageBar>
        ) : rows.length === 0 ? (
          <Body1 className={s.muted}>{search ? 'No tenants match your search.' : 'No tenants yet.'}</Body1>
        ) : (
          <div className={s.tableWrap}>
            <Table aria-label="Tenants" size="small">
              <TableHeader>
                <TableRow>
                  <TableHeaderCell>Tenant</TableHeaderCell>
                  <TableHeaderCell>Status</TableHeaderCell>
                  <TableHeaderCell>D365</TableHeaderCell>
                  <TableHeaderCell>Users</TableHeaderCell>
                  <TableHeaderCell>Created</TableHeaderCell>
                </TableRow>
              </TableHeader>
              <TableBody>
                {rows.map((t) => (
                  <TableRow key={t.id} className={s.row} tabIndex={0}
                    onClick={() => navigate(`/platform/tenants/${t.id}`)}
                    onKeyDown={(e) => e.key === 'Enter' && navigate(`/platform/tenants/${t.id}`)}>
                    <TableCell>
                      <TableCellLayout media={<Building20Regular />} description={t.slug}>{t.name}</TableCellLayout>
                    </TableCell>
                    <TableCell>
                      {t.isActive ? <Badge appearance="tint" color="success">Active</Badge> : <Badge appearance="tint" color="subtle">Disabled</Badge>}
                    </TableCell>
                    <TableCell><ConnectionStatusBadge connection={t.connection} /></TableCell>
                    <TableCell>{t.userCount}</TableCell>
                    <TableCell>{new Date(t.createdAt).toLocaleDateString()}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </Card>

      <Dialog open={open} onOpenChange={(_, d) => setOpen(d.open)}>
        <DialogSurface aria-describedby={undefined}>
          <DialogBody>
            <DialogTitle>New tenant</DialogTitle>
            <DialogContent className={s.form}>
              {serverError && <MessageBar intent="error"><MessageBarBody>{serverError}</MessageBarBody></MessageBar>}
              <Field label="Organization name" required validationMessage={errors.name}>
                <Input value={name} onChange={(_, d) => { setName(d.value); if (!slugTouched) setSlug(slugify(d.value)); }} />
              </Field>
              <Field label="Slug" required validationMessage={errors.slug} hint="Short unique key, e.g. contoso-eg">
                <Input value={slug} onChange={(_, d) => { setSlug(d.value); setSlugTouched(true); }} />
              </Field>
              <Field label="Allowed Microsoft Entra tenant IDs" validationMessage={errors.allowedEntraTenantIds}
                hint="Directory IDs whose users may sign in to this tenant (one per line). Usually the customer's own tenant ID.">
                <Textarea value={dirs} onChange={(_, d) => setDirs(d.value)} resize="vertical" />
              </Field>
              <Caption1 className={s.muted}>You’ll add the D365 connection and the first tenant admin on the next screen.</Caption1>
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={() => setOpen(false)} disabled={create.isPending}>Cancel</Button>
              <Button appearance="primary" onClick={submit} disabled={create.isPending}>{create.isPending ? 'Creating…' : 'Create tenant'}</Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </div>
  );
}
