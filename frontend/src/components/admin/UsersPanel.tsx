import { useMemo, useState } from 'react';
import {
  Badge,
  Body1,
  Button,
  Caption1,
  Card,
  Input,
  MessageBar,
  MessageBarBody,
  Skeleton,
  SkeletonItem,
  Subtitle2,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { Edit16Regular, PersonAdd20Regular, Search16Regular } from '@fluentui/react-icons';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { roleLabel, type AdminScope, type User, type UserForm } from '@/api/admin';
import { errorMessage, useNotify } from '@/components/notify';
import { UserDialog } from './UserDialog';

const useStyles = makeStyles({
  card: { padding: '20px', rowGap: '12px' },
  toolbar: { display: 'flex', alignItems: 'center', columnGap: '12px', flexWrap: 'wrap' },
  spacer: { flexGrow: 1 },
  tableWrap: { overflowX: 'auto' },
  muted: { color: tokens.colorNeutralForeground3 },
  mono: { fontFamily: tokens.fontFamilyMonospace },
});

export function UsersPanel({ scope, title = 'Users' }: { scope: AdminScope; title?: string }) {
  const s = useStyles();
  const qc = useQueryClient();
  const notify = useNotify();
  const key = [...scope.key, 'users'];
  const users = useQuery({ queryKey: key, queryFn: () => scope.listUsers() });

  const [search, setSearch] = useState('');
  const [dialog, setDialog] = useState<{ open: boolean; user: User | null }>({ open: false, user: null });
  const [serverError, setServerError] = useState<string | null>(null);

  const save = useMutation({
    mutationFn: (form: UserForm) =>
      dialog.user
        ? scope.updateUser(dialog.user.id, {
            displayName: form.displayName, role: form.role, companies: form.companies,
            defaultCompany: form.defaultCompany, isActive: form.isActive,
          })
        : scope.createUser(form),
    onSuccess: (u) => {
      qc.invalidateQueries({ queryKey: key });
      setDialog({ open: false, user: null });
      notify.success(dialog.user ? 'User updated' : 'User added', u.email);
    },
    onError: (e) => setServerError(errorMessage(e)),
  });

  const rows = useMemo(() => {
    const q = search.trim().toLowerCase();
    return (users.data ?? []).filter(
      (u) => !q || u.email.includes(q) || u.displayName.toLowerCase().includes(q) || u.companies.some((c) => c.toLowerCase().includes(q)),
    );
  }, [users.data, search]);

  const open = (user: User | null) => {
    setServerError(null);
    setDialog({ open: true, user });
  };

  return (
    <Card className={s.card}>
      <div className={s.toolbar}>
        <Subtitle2 as="h2">{title}</Subtitle2>
        <span className={s.spacer} />
        <Input contentBefore={<Search16Regular />} placeholder="Search users" value={search}
          onChange={(_, d) => setSearch(d.value)} aria-label="Search users" />
        <Button appearance="primary" icon={<PersonAdd20Regular />} onClick={() => open(null)}>Add user</Button>
      </div>

      {users.isLoading ? (
        <Skeleton aria-label="Loading users">
          <SkeletonItem size={32} />
          <SkeletonItem size={32} />
          <SkeletonItem size={32} />
        </Skeleton>
      ) : users.isError ? (
        <MessageBar intent="error">
          <MessageBarBody>{errorMessage(users.error)}</MessageBarBody>
        </MessageBar>
      ) : rows.length === 0 ? (
        <Body1 className={s.muted}>{search ? 'No users match your search.' : 'No users yet. Add the first one.'}</Body1>
      ) : (
        <div className={s.tableWrap}>
          <Table aria-label={title} size="small">
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Name</TableHeaderCell>
                <TableHeaderCell>Role</TableHeaderCell>
                <TableHeaderCell>Companies</TableHeaderCell>
                <TableHeaderCell>Status</TableHeaderCell>
                <TableHeaderCell>Last sign-in</TableHeaderCell>
                <TableHeaderCell aria-label="Actions" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.map((u) => (
                <TableRow key={u.id}>
                  <TableCell>
                    <div>{u.displayName}</div>
                    <Caption1 className={s.muted}>{u.email}</Caption1>
                  </TableCell>
                  <TableCell>{roleLabel(u.role)}</TableCell>
                  <TableCell>
                    <span className={s.mono}>{u.companies.map((c) => (c === u.defaultCompany ? `${c}*` : c)).join(', ') || '—'}</span>
                  </TableCell>
                  <TableCell>
                    {!u.isActive ? (
                      <Badge appearance="tint" color="subtle">Deactivated</Badge>
                    ) : u.isSignedInOnce ? (
                      <Badge appearance="tint" color="success">Active</Badge>
                    ) : (
                      <Badge appearance="tint" color="warning">Invited</Badge>
                    )}
                  </TableCell>
                  <TableCell>{u.lastLoginAt ? new Date(u.lastLoginAt).toLocaleString() : '—'}</TableCell>
                  <TableCell>
                    <Button size="small" appearance="subtle" icon={<Edit16Regular />} onClick={() => open(u)}
                      aria-label={`Edit ${u.displayName}`} />
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <Caption1 className={s.muted}>* default company</Caption1>
        </div>
      )}

      <UserDialog scope={scope} open={dialog.open} user={dialog.user} saving={save.isPending} serverError={serverError}
        onCancel={() => setDialog({ open: false, user: null })} onSubmit={(f) => save.mutate(f)} />
    </Card>
  );
}
