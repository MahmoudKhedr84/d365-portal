import { useEffect, useState } from 'react';
import {
  Badge,
  Body1,
  Button,
  Caption1,
  Card,
  Field,
  Input,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Skeleton,
  SkeletonItem,
  Subtitle2,
  Switch,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { PlugConnected20Regular, Save20Regular } from '@fluentui/react-icons';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { connectionFormSchema, type AdminScope, type Connection, type ConnectionForm, type TestResult } from '@/api/admin';
import { errorMessage, fieldErrors, useNotify } from '@/components/notify';
import { EntityCheckPanel } from './EntityCheckPanel';

const useStyles = makeStyles({
  card: { padding: '20px', rowGap: '16px', maxWidth: '760px' },
  header: { display: 'flex', alignItems: 'center', columnGap: '12px', flexWrap: 'wrap' },
  grid: {
    display: 'grid',
    gap: '12px 16px',
    gridTemplateColumns: '1fr',
    '@media (min-width: 720px)': { gridTemplateColumns: '1fr 1fr' },
  },
  full: { '@media (min-width: 720px)': { gridColumn: '1 / -1' } },
  actions: { display: 'flex', columnGap: '8px', flexWrap: 'wrap', alignItems: 'center' },
  help: { color: tokens.colorNeutralForeground3 },
});

const empty: ConnectionForm = {
  environmentUrl: '',
  entraTenantId: '',
  clientId: '',
  clientSecret: '',
  apiVersion: '',
  isActive: true,
  useExtendedProductDimensions: false,
};

const fromConnection = (c: Connection): ConnectionForm => ({
  environmentUrl: c.environmentUrl,
  entraTenantId: c.entraTenantId,
  clientId: c.clientId,
  clientSecret: '',
  apiVersion: c.apiVersion ?? '',
  isActive: c.isActive,
  useExtendedProductDimensions: c.useExtendedProductDimensions ?? false,
});

export function ConnectionStatusBadge({ connection }: { connection: Connection | null | undefined }) {
  if (!connection) return <Badge appearance="tint" color="informative">Not set up</Badge>;
  if (!connection.isActive) return <Badge appearance="tint" color="subtle">Inactive</Badge>;
  if (connection.lastTestSucceeded === true) return <Badge appearance="tint" color="success">Connected</Badge>;
  if (connection.lastTestSucceeded === false) return <Badge appearance="tint" color="danger">Test failed</Badge>;
  return <Badge appearance="tint" color="warning">Not tested</Badge>;
}

/** D365 connection form with Save and Test connection. Used by tenant admins and super-admins. */
export function ConnectionPanel({ scope }: { scope: AdminScope }) {
  const s = useStyles();
  const notify = useNotify();
  const qc = useQueryClient();
  const key = [...scope.key, 'connection'];

  const query = useQuery({ queryKey: key, queryFn: () => scope.getConnection() });
  const [form, setForm] = useState<ConnectionForm>(empty);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [lastTest, setLastTest] = useState<TestResult | null>(null);

  useEffect(() => {
    if (query.data) setForm(fromConnection(query.data));
  }, [query.data]);

  const save = useMutation({
    mutationFn: (f: ConnectionForm) => scope.saveConnection(f),
    onSuccess: (c) => {
      qc.setQueryData(key, c);
      qc.invalidateQueries({ queryKey: [...scope.key, 'companies'] });
      setLastTest(null);
      notify.success('Connection saved', 'Run "Test connection" to verify it.');
    },
    onError: (e) => notify.error('Could not save the connection', e),
  });

  const test = useMutation({
    mutationFn: () => scope.testConnection(),
    onSuccess: (r) => {
      setLastTest(r);
      qc.invalidateQueries({ queryKey: key });
      if (r.success) qc.invalidateQueries({ queryKey: [...scope.key, 'companies'] });
    },
    onError: (e) => setLastTest({ success: false, message: errorMessage(e), durationMs: 0, testedAt: new Date().toISOString() }),
  });

  const set = <K extends keyof ConnectionForm>(k: K, v: ConnectionForm[K]) => setForm((f) => ({ ...f, [k]: v }));

  const onSave = () => {
    const parsed = connectionFormSchema.safeParse(form);
    const errs = parsed.success ? {} : fieldErrors(parsed.error.issues);
    if (!query.data?.hasClientSecret && !form.clientSecret?.trim()) errs.clientSecret = 'Client secret is required for a new connection.';
    setErrors(errs);
    if (Object.keys(errs).length === 0 && parsed.success) save.mutate(parsed.data);
  };

  const connection = query.data;
  const dirty = connection ? JSON.stringify(fromConnection(connection)) !== JSON.stringify(form) : true;

  return (
    <Card className={s.card}>
      <div className={s.header}>
        <PlugConnected20Regular />
        <Subtitle2 as="h2">D365 F&amp;O connection</Subtitle2>
        <ConnectionStatusBadge connection={connection} />
      </div>
      <Caption1 className={s.help}>
        Service-to-service access (client credentials). Register the app&apos;s client ID in D365 under{' '}
        <strong>System administration &gt; Setup &gt; Microsoft Entra ID applications</strong> and map it to a D365 user
        with the needed security roles. The secret is encrypted and never shown again.
      </Caption1>

      {query.isLoading ? (
        <Skeleton aria-label="Loading connection">
          <SkeletonItem size={32} />
          <SkeletonItem size={32} />
          <SkeletonItem size={32} />
        </Skeleton>
      ) : query.isError ? (
        <MessageBar intent="error">
          <MessageBarBody>{errorMessage(query.error)}</MessageBarBody>
        </MessageBar>
      ) : (
        <>
          <div className={s.grid}>
            <Field className={s.full} label="Environment URL" required validationMessage={errors.environmentUrl}
              hint="e.g. https://contoso.operations.dynamics.com">
              <Input value={form.environmentUrl} onChange={(_, d) => set('environmentUrl', d.value)} placeholder="https://xxx.operations.dynamics.com" />
            </Field>
            <Field label="Entra tenant ID (of the service app)" required validationMessage={errors.entraTenantId}>
              <Input value={form.entraTenantId} onChange={(_, d) => set('entraTenantId', d.value)} />
            </Field>
            <Field label="Client ID" required validationMessage={errors.clientId}>
              <Input value={form.clientId} onChange={(_, d) => set('clientId', d.value)} />
            </Field>
            <Field label="Client secret" required={!connection?.hasClientSecret} validationMessage={errors.clientSecret}
              hint={connection?.hasClientSecret ? 'Leave empty to keep the stored secret.' : 'The secret Value (not the Secret ID).'}>
              <Input type="password" autoComplete="new-password" value={form.clientSecret ?? ''}
                placeholder={connection?.hasClientSecret ? '•••••••• (stored)' : ''}
                onChange={(_, d) => set('clientSecret', d.value)} />
            </Field>
            <Field label="API version (optional)" validationMessage={errors.apiVersion}>
              <Input value={form.apiVersion ?? ''} onChange={(_, d) => set('apiVersion', d.value)} />
            </Field>
            <Field className={s.full}>
              <Switch checked={form.isActive} label={form.isActive ? 'Active' : 'Inactive'} onChange={(_, d) => set('isActive', d.checked)} />
            </Field>
            <Field className={s.full}
              hint="Turn on only for environments where the procurement line and released product variant entities were extended with InventDimension1-3 (e.g. YELO's Model / Type / Year). Items that use them then ask for those values.">
              <Switch checked={!!form.useExtendedProductDimensions} label="Extended product dimensions (Model / Type / Year)"
                onChange={(_, d) => set('useExtendedProductDimensions', d.checked)} />
            </Field>
          </div>

          <div className={s.actions}>
            <Button appearance="primary" icon={<Save20Regular />} onClick={onSave} disabled={save.isPending || (!dirty && !!connection?.hasClientSecret)}>
              {save.isPending ? 'Saving…' : 'Save'}
            </Button>
            <Button icon={<PlugConnected20Regular />} onClick={() => test.mutate()} disabled={!connection?.hasClientSecret || dirty || test.isPending}>
              {test.isPending ? 'Testing…' : 'Test connection'}
            </Button>
            {connection && dirty && <Caption1 className={s.help}>Save your changes before testing.</Caption1>}
          </div>

          {lastTest ? (
            <MessageBar intent={lastTest.success ? 'success' : 'error'}>
              <MessageBarBody>
                <MessageBarTitle>{lastTest.success ? 'Connection works' : 'Connection failed'}</MessageBarTitle>
                {lastTest.message} {lastTest.durationMs > 0 && <Caption1>({lastTest.durationMs} ms)</Caption1>}
              </MessageBarBody>
            </MessageBar>
          ) : (
            connection?.lastTestedAt && (
              <Body1 className={s.help}>
                Last tested {new Date(connection.lastTestedAt).toLocaleString()}: {connection.lastTestResult}
              </Body1>
            )
          )}

          <EntityCheckPanel scope={scope} disabled={!connection?.hasClientSecret || dirty || !connection?.isActive} />
        </>
      )}
    </Card>
  );
}
