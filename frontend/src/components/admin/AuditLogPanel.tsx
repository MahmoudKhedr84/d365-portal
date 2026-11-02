import { Fragment, useState } from 'react';
import {
  Badge,
  Button,
  Caption1,
  Card,
  Dropdown,
  Input,
  MessageBar,
  MessageBarBody,
  Option,
  Spinner,
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
import { ChevronDown16Regular, ChevronLeft20Regular, ChevronRight16Regular, ChevronRight20Regular, Search20Regular } from '@fluentui/react-icons';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useApi } from '@/api/ApiProvider';
import { getAudit, type AuditFilter } from '@/api/audit';
import { useDebounced } from '@/components/LookupCombobox';
import { errorMessage } from '@/components/notify';

const PAGE = 50;

const useStyles = makeStyles({
  card: { padding: '16px', rowGap: '12px' },
  toolbar: { display: 'flex', columnGap: '12px', rowGap: '8px', flexWrap: 'wrap', alignItems: 'center' },
  search: { minWidth: '240px', flexGrow: 1, maxWidth: '420px' },
  scroll: { overflowX: 'auto' },
  table: { minWidth: '900px' },
  mono: { fontFamily: tokens.fontFamilyMonospace, fontSize: tokens.fontSizeBase200 },
  detail: {
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
    whiteSpace: 'pre-wrap',
    overflowWrap: 'anywhere',
    backgroundColor: tokens.colorNeutralBackground3,
    padding: '8px 12px',
    borderRadius: tokens.borderRadiusMedium,
  },
  muted: { color: tokens.colorNeutralForeground3 },
  pager: { display: 'flex', alignItems: 'center', columnGap: '8px', justifyContent: 'flex-end' },
});

const ACTIONS = [
  { value: '', label: 'All actions' },
  { value: 'D365.', label: 'D365 calls' },
  { value: 'Create', label: 'Created documents' },
  { value: 'Login', label: 'Sign-ins' },
  { value: 'Update', label: 'Admin changes' },
];

/** Audit log viewer: sign-ins, admin changes and every D365 call, newest first. */
export function AuditLogPanel({ base, tenantScoped, queryKey }: { base: string; tenantScoped: boolean; queryKey: readonly unknown[] }) {
  const s = useStyles();
  const api = useApi();
  const [search, setSearch] = useState('');
  const [success, setSuccess] = useState<AuditFilter['success']>('');
  const [action, setAction] = useState('');
  const [skip, setSkip] = useState(0);
  const [open, setOpen] = useState<number | null>(null);
  const q = useDebounced(search);

  const log = useQuery({
    queryKey: [...queryKey, 'audit', q, success, action, skip],
    queryFn: ({ signal }) => getAudit(api, base, tenantScoped, { search: q, success, action, top: PAGE, skip }, signal),
    placeholderData: keepPreviousData,
  });

  return (
    <Card className={s.card}>
      <Subtitle2 as="h2">Audit log</Subtitle2>
      <div className={s.toolbar}>
        <Input className={s.search} contentBefore={<Search20Regular />} placeholder="Search user, entity or message" value={search}
          aria-label="Search audit log" onChange={(_, d) => { setSearch(d.value); setSkip(0); }} />
        <Dropdown aria-label="Action" value={ACTIONS.find((a) => a.value === action)?.label} selectedOptions={[action]}
          onOptionSelect={(_, d) => { setAction(d.optionValue ?? ''); setSkip(0); }}>
          {ACTIONS.map((a) => <Option key={a.value} value={a.value}>{a.label}</Option>)}
        </Dropdown>
        <Dropdown aria-label="Result" value={success === 'false' ? 'Failures only' : success === 'true' ? 'Successes only' : 'All results'}
          selectedOptions={[success ?? '']} onOptionSelect={(_, d) => { setSuccess((d.optionValue ?? '') as AuditFilter['success']); setSkip(0); }}>
          <Option value="">All results</Option>
          <Option value="false">Failures only</Option>
          <Option value="true">Successes only</Option>
        </Dropdown>
      </div>
      {log.isError ? (
        <MessageBar intent="error"><MessageBarBody>{errorMessage(log.error)}</MessageBarBody></MessageBar>
      ) : log.isLoading ? (
        <Spinner label="Loading audit log…" />
      ) : (
        <>
          <div className={s.scroll}>
            <Table size="small" aria-label="Audit log" className={s.table}>
              <TableHeader>
                <TableRow>
                  <TableHeaderCell />
                  <TableHeaderCell>When</TableHeaderCell>
                  <TableHeaderCell>User</TableHeaderCell>
                  <TableHeaderCell>Company</TableHeaderCell>
                  <TableHeaderCell>Action</TableHeaderCell>
                  <TableHeaderCell>Entity</TableHeaderCell>
                  <TableHeaderCell>Result</TableHeaderCell>
                  <TableHeaderCell>Duration</TableHeaderCell>
                </TableRow>
              </TableHeader>
              <TableBody>
                {log.data!.items.length === 0 && (
                  <TableRow><TableCell colSpan={8}><Caption1 className={s.muted}>No entries.</Caption1></TableCell></TableRow>
                )}
                {log.data!.items.map((e) => (
                  <Fragment key={e.id}>
                    <TableRow>
                      <TableCell>
                        <Button appearance="subtle" size="small" aria-label={open === e.id ? 'Hide details' : 'Show details'}
                          icon={open === e.id ? <ChevronDown16Regular /> : <ChevronRight16Regular />}
                          onClick={() => setOpen(open === e.id ? null : e.id)} />
                      </TableCell>
                      <TableCell>{new Date(e.timestamp).toLocaleString()}</TableCell>
                      <TableCell>{e.userEmail}</TableCell>
                      <TableCell className={s.mono}>{e.company}</TableCell>
                      <TableCell className={s.mono}>{e.action}</TableCell>
                      <TableCell className={s.mono}>{e.entity}</TableCell>
                      <TableCell>
                        <Badge appearance="tint" color={e.success ? 'success' : 'danger'}>
                          {e.success ? 'OK' : 'Failed'}{e.statusCode ? ` ${e.statusCode}` : ''}
                        </Badge>
                      </TableCell>
                      <TableCell>{e.durationMs !== null ? `${e.durationMs} ms` : ''}</TableCell>
                    </TableRow>
                    {open === e.id && (
                      <TableRow>
                        <TableCell colSpan={8}>
                          <div className={s.detail}>
                            {e.requestSummary && <>Request: {e.requestSummary}{'\n'}</>}
                            {e.responseSummary && <>Response: {e.responseSummary}{'\n'}</>}
                            {e.correlationId && <>Correlation: {e.correlationId}</>}
                          </div>
                        </TableCell>
                      </TableRow>
                    )}
                  </Fragment>
                ))}
              </TableBody>
            </Table>
          </div>
          <div className={s.pager}>
            <Caption1 className={s.muted}>{`${Math.min(skip + 1, log.data!.total)}–${Math.min(skip + PAGE, log.data!.total)} of ${log.data!.total}`}</Caption1>
            <Button aria-label="Previous page" icon={<ChevronLeft20Regular />} disabled={skip === 0 || log.isFetching}
              onClick={() => setSkip(Math.max(0, skip - PAGE))} />
            <Button aria-label="Next page" icon={<ChevronRight20Regular />} disabled={log.isFetching || skip + PAGE >= log.data!.total}
              onClick={() => setSkip(skip + PAGE)} />
          </div>
        </>
      )}
    </Card>
  );
}
