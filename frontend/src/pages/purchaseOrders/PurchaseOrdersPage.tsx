import { useState } from 'react';
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
import { PO_APPROVALS, PO_STATUSES, listPurchaseOrders, poApprovalLabel, poStatusLabel } from '@/api/purchaseOrders';
import { useDebounced } from '@/components/LookupCombobox';
import { errorMessage } from '@/components/notify';
import { formatDate } from '@/pages/purchaseRequisitions/shared';
import { useSessionStore } from '@/state/sessionStore';

const PAGE = 25;

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', rowGap: '16px' },
  head: { display: 'flex', alignItems: 'center', columnGap: '12px', flexWrap: 'wrap', rowGap: '8px' },
  spacer: { flexGrow: 1 },
  card: { padding: '16px', rowGap: '12px' },
  toolbar: { display: 'flex', columnGap: '12px', rowGap: '8px', flexWrap: 'wrap' },
  search: { minWidth: '240px', flexGrow: 1, maxWidth: '420px' },
  scroll: { overflowX: 'auto' },
  table: { minWidth: '820px' },
  mono: { fontFamily: tokens.fontFamilyMonospace },
  muted: { color: tokens.colorNeutralForeground3 },
  pager: { display: 'flex', alignItems: 'center', columnGap: '8px', justifyContent: 'flex-end' },
});

export function PoStatusBadges({ status, approval }: { status: string | null; approval: string | null }) {
  const approvalColor = approval === 'Approved' || approval === 'Confirmed' || approval === 'Finalized' ? 'success'
    : approval === 'Rejected' ? 'danger' : approval === 'InReview' ? 'warning' : 'informative';
  return (
    <span style={{ display: 'inline-flex', gap: 4, flexWrap: 'wrap' }}>
      <Badge appearance="tint" color={status === 'Canceled' ? 'subtle' : 'brand'}>{poStatusLabel(status)}</Badge>
      <Badge appearance="outline" color={approvalColor}>{poApprovalLabel(approval)}</Badge>
    </span>
  );
}

export function PurchaseOrdersPage() {
  const s = useStyles();
  const api = useApi();
  const navigate = useNavigate();
  const tenantId = useSessionStore((st) => st.tenantId);
  const company = useSessionStore((st) => st.company);
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState('');
  const [approval, setApproval] = useState('');
  const [skip, setSkip] = useState(0);
  const q = useDebounced(search);

  const list = useQuery({
    queryKey: ['po', tenantId, company, 'list', q, status, approval, skip],
    queryFn: ({ signal }) => listPurchaseOrders(api, { search: q, status, approval, top: PAGE, skip }, signal),
    placeholderData: keepPreviousData,
    enabled: !!company,
  });
  const total = list.data?.total ?? null;

  return (
    <div className={s.page}>
      <div className={s.head}>
        <Title2 as="h1">Purchase orders</Title2>
        <Badge appearance="tint" className={s.mono}>{company ?? '—'}</Badge>
        <span className={s.spacer} />
        <Button appearance="primary" icon={<Add20Regular />} onClick={() => navigate('/purchase-orders/new')} disabled={!company}>
          New purchase order
        </Button>
      </div>

      <Card className={s.card}>
        <div className={s.toolbar}>
          <Input className={s.search} contentBefore={<Search20Regular />} placeholder="Search number, vendor or name" value={search}
            aria-label="Search purchase orders" onChange={(_, d) => { setSearch(d.value); setSkip(0); }} />
          <Dropdown aria-label="Status" value={status ? poStatusLabel(status) : 'All statuses'} selectedOptions={[status]}
            onOptionSelect={(_, d) => { setStatus(d.optionValue ?? ''); setSkip(0); }}>
            <Option value="">All statuses</Option>
            {PO_STATUSES.map((st) => <Option key={st} value={st}>{poStatusLabel(st)}</Option>)}
          </Dropdown>
          <Dropdown aria-label="Approval" value={approval ? poApprovalLabel(approval) : 'Any approval'} selectedOptions={[approval]}
            onOptionSelect={(_, d) => { setApproval(d.optionValue ?? ''); setSkip(0); }}>
            <Option value="">Any approval</Option>
            {PO_APPROVALS.map((st) => <Option key={st} value={st}>{poApprovalLabel(st)}</Option>)}
          </Dropdown>
        </div>

        {!company ? (
          <Caption1 className={s.muted}>Select a company in the top bar.</Caption1>
        ) : list.isError ? (
          <MessageBar intent="error"><MessageBarBody>{errorMessage(list.error)}</MessageBarBody></MessageBar>
        ) : list.isLoading ? (
          <Spinner label="Loading purchase orders from D365…" />
        ) : (
          <>
            <div className={s.scroll}>
              <Table aria-label="Purchase orders" size="small" className={s.table}>
                <TableHeader>
                  <TableRow>
                    <TableHeaderCell>Number</TableHeaderCell>
                    <TableHeaderCell>Vendor</TableHeaderCell>
                    <TableHeaderCell>Status</TableHeaderCell>
                    <TableHeaderCell>Currency</TableHeaderCell>
                    <TableHeaderCell>Delivery</TableHeaderCell>
                    <TableHeaderCell>Reference</TableHeaderCell>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {list.data!.items.length === 0 && (
                    <TableRow><TableCell colSpan={6}><Caption1 className={s.muted}>No purchase orders found.</Caption1></TableCell></TableRow>
                  )}
                  {list.data!.items.map((r) => (
                    <TableRow key={r.purchaseOrderNumber}>
                      <TableCell className={s.mono}>
                        <Link to={`/purchase-orders/${encodeURIComponent(r.purchaseOrderNumber)}`}>{r.purchaseOrderNumber}</Link>
                      </TableCell>
                      <TableCell><span className={s.mono}>{r.vendor}</span> {r.vendorName}</TableCell>
                      <TableCell><PoStatusBadges status={r.status} approval={r.approvalStatus} /></TableCell>
                      <TableCell>{r.currency}</TableCell>
                      <TableCell>{formatDate(r.requestedDeliveryDate)}</TableCell>
                      <TableCell>{r.vendorReference}</TableCell>
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
