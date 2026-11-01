import { useState } from 'react';
import {
  Body1,
  Button,
  Caption1,
  Card,
  Dropdown,
  Input,
  MessageBar,
  MessageBarBody,
  Option,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Title2,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { Add20Regular, ChevronLeft20Regular, ChevronRight20Regular, Search20Regular } from '@fluentui/react-icons';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Link, useNavigate } from 'react-router-dom';
import { useApi } from '@/api/ApiProvider';
import { PR_STATUSES, listPurchaseRequisitions, prStatusLabel } from '@/api/purchaseRequisitions';
import { useDebounced } from '@/components/LookupCombobox';
import { errorMessage } from '@/components/notify';
import { useSessionStore } from '@/state/sessionStore';
import { PrStatusBadge, formatDate } from './shared';

const PAGE = 25;

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', rowGap: '16px' },
  head: { display: 'flex', alignItems: 'center', columnGap: '12px', flexWrap: 'wrap', rowGap: '8px' },
  spacer: { flexGrow: 1 },
  card: { padding: '16px', rowGap: '12px' },
  toolbar: { display: 'flex', columnGap: '12px', rowGap: '8px', flexWrap: 'wrap' },
  search: { minWidth: '240px', flexGrow: 1, maxWidth: '420px' },
  scroll: { overflowX: 'auto' },
  table: { minWidth: '720px' },
  mono: { fontFamily: tokens.fontFamilyMonospace },
  muted: { color: tokens.colorNeutralForeground3 },
  pager: { display: 'flex', alignItems: 'center', columnGap: '8px', justifyContent: 'flex-end' },
});

export function PurchaseRequisitionsPage() {
  const s = useStyles();
  const api = useApi();
  const navigate = useNavigate();
  const tenantId = useSessionStore((st) => st.tenantId);
  const company = useSessionStore((st) => st.company);
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState('');
  const [skip, setSkip] = useState(0);
  const q = useDebounced(search);

  const list = useQuery({
    queryKey: ['pr', tenantId, company, 'list', q, status, skip],
    queryFn: ({ signal }) => listPurchaseRequisitions(api, { search: q, status, top: PAGE, skip }, signal),
    placeholderData: keepPreviousData,
  });
  const total = list.data?.total ?? null;

  return (
    <div className={s.page}>
      <div className={s.head}>
        <Title2 as="h1">Purchase requisitions</Title2>
        <span className={s.spacer} />
        <Button appearance="primary" icon={<Add20Regular />} onClick={() => navigate('/purchase-requisitions/new')} disabled={!company}>
          New requisition
        </Button>
      </div>
      <Body1 className={s.muted}>
        Requisitions are shared across companies in D365; new lines are created for company <strong>{company ?? '—'}</strong>.
      </Body1>

      <Card className={s.card}>
        <div className={s.toolbar}>
          <Input className={s.search} contentBefore={<Search20Regular />} placeholder="Search number or name" value={search}
            aria-label="Search requisitions" onChange={(_, d) => { setSearch(d.value); setSkip(0); }} />
          <Dropdown aria-label="Status" placeholder="All statuses" value={status ? prStatusLabel(status) : 'All statuses'}
            selectedOptions={[status]} onOptionSelect={(_, d) => { setStatus(d.optionValue ?? ''); setSkip(0); }}>
            <Option value="">All statuses</Option>
            {PR_STATUSES.map((st) => <Option key={st} value={st}>{prStatusLabel(st)}</Option>)}
          </Dropdown>
        </div>

        {list.isError ? (
          <MessageBar intent="error"><MessageBarBody>{errorMessage(list.error)}</MessageBarBody></MessageBar>
        ) : list.isLoading ? (
          <Spinner label="Loading requisitions from D365…" />
        ) : (
          <>
            <div className={s.scroll}>
              <Table aria-label="Purchase requisitions" size="small" className={s.table}>
                <TableHeader>
                  <TableRow>
                    <TableHeaderCell>Number</TableHeaderCell>
                    <TableHeaderCell>Name</TableHeaderCell>
                    <TableHeaderCell>Status</TableHeaderCell>
                    <TableHeaderCell>Purpose</TableHeaderCell>
                    <TableHeaderCell>Preparer</TableHeaderCell>
                    <TableHeaderCell>Requested</TableHeaderCell>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {list.data!.items.length === 0 && (
                    <TableRow><TableCell colSpan={6}><Caption1 className={s.muted}>No requisitions found.</Caption1></TableCell></TableRow>
                  )}
                  {list.data!.items.map((r) => (
                    <TableRow key={r.requisitionNumber}>
                      <TableCell className={s.mono}>
                        <Link to={`/purchase-requisitions/${encodeURIComponent(r.requisitionNumber)}`}>{r.requisitionNumber}</Link>
                      </TableCell>
                      <TableCell>{r.name}</TableCell>
                      <TableCell><PrStatusBadge status={r.status} /></TableCell>
                      <TableCell>{r.purpose}</TableCell>
                      <TableCell className={s.mono}>{r.preparer}</TableCell>
                      <TableCell>{formatDate(r.requestedDate)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
            <div className={s.pager}>
              <Caption1 className={s.muted}>
                {total !== null ? `${Math.min(skip + 1, total)}–${Math.min(skip + PAGE, total)} of ${total}` : ''}
              </Caption1>
              <Button aria-label="Previous page" icon={<ChevronLeft20Regular />} disabled={skip === 0 || list.isFetching}
                onClick={() => setSkip(Math.max(0, skip - PAGE))} />
              <Button aria-label="Next page" icon={<ChevronRight20Regular />}
                disabled={list.isFetching || (total !== null ? skip + PAGE >= total : list.data!.items.length < PAGE)}
                onClick={() => setSkip(skip + PAGE)} />
            </div>
          </>
        )}
      </Card>
    </div>
  );
}
