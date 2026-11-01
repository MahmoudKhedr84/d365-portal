import {
  Badge,
  Button,
  Caption1,
  MessageBar,
  MessageBarBody,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Tooltip,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { TableSearch20Regular } from '@fluentui/react-icons';
import { useMutation } from '@tanstack/react-query';
import type { AdminScope } from '@/api/admin';
import type { EntityCheckRow } from '@/api/lookups';
import { errorMessage } from '@/components/notify';

const useStyles = makeStyles({
  wrap: { display: 'flex', flexDirection: 'column', rowGap: '8px' },
  scroll: { overflowX: 'auto' },
  mono: { fontFamily: tokens.fontFamilyMonospace, fontSize: tokens.fontSizeBase200 },
  muted: { color: tokens.colorNeutralForeground3 },
});

function StatusBadge({ row }: { row: EntityCheckRow }) {
  if (!row.found) return <Badge appearance="tint" color="danger">Missing</Badge>;
  if (!row.fieldsConfirmed) return <Badge appearance="tint" color="warning">No data</Badge>;
  return <Badge appearance="tint" color="success">OK</Badge>;
}

/** "Check D365 entities": shows which standard entities and fields exist in the environment. */
export function EntityCheckPanel({ scope, disabled }: { scope: AdminScope; disabled?: boolean }) {
  const s = useStyles();
  const check = useMutation({ mutationFn: () => scope.checkEntities() });
  const rows = check.data ?? [];
  const missing = rows.filter((r) => !r.found).length;

  return (
    <div className={s.wrap}>
      <div>
        <Button icon={<TableSearch20Regular />} onClick={() => check.mutate()} disabled={disabled || check.isPending}>
          {check.isPending ? 'Checking entities…' : 'Check D365 entities'}
        </Button>
      </div>
      {check.isError && (
        <MessageBar intent="error">
          <MessageBarBody>{errorMessage(check.error)}</MessageBarBody>
        </MessageBar>
      )}
      {rows.length > 0 && (
        <>
          <Caption1 className={s.muted}>
            {missing === 0 ? 'All entities were found.' : `${missing} entit${missing === 1 ? 'y was' : 'ies were'} not found.`}{' '}
            “No data” means the entity exists but has no rows, so field names could not be confirmed.
          </Caption1>
          <div className={s.scroll}>
            <Table size="small" aria-label="D365 entity check">
              <TableHeader>
                <TableRow>
                  <TableHeaderCell>Used for</TableHeaderCell>
                  <TableHeaderCell>Status</TableHeaderCell>
                  <TableHeaderCell>Entity</TableHeaderCell>
                  <TableHeaderCell>Id / name fields</TableHeaderCell>
                  <TableHeaderCell>Per company</TableHeaderCell>
                </TableRow>
              </TableHeader>
              <TableBody>
                {rows.map((r) => (
                  <TableRow key={r.key}>
                    <TableCell>{r.label}</TableCell>
                    <TableCell>
                      {r.error ? (
                        <Tooltip content={r.error} relationship="description">
                          <span><StatusBadge row={r} /></span>
                        </Tooltip>
                      ) : (
                        <StatusBadge row={r} />
                      )}
                    </TableCell>
                    <TableCell className={s.mono}>{r.entitySet ?? `tried ${r.tried.join(', ')}`}</TableCell>
                    <TableCell className={s.mono}>
                      {r.kind === 'lookup'
                        ? [r.idField, r.nameField].filter(Boolean).join(' / ')
                        : r.sampleFields.length > 0
                          ? `${r.sampleFields.length} fields`
                          : '—'}
                    </TableCell>
                    <TableCell>{r.companySpecific == null ? '—' : r.companySpecific ? 'Yes' : 'No'}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        </>
      )}
    </div>
  );
}
