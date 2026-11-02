import { useState } from 'react';
import {
  Body1,
  Button,
  Caption1,
  Card,
  MessageBar,
  MessageBarBody,
  Skeleton,
  SkeletonItem,
  Subtitle2,
  Tooltip,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import {
  Add16Regular,
  ArrowClockwise20Regular,
  BookOpen20Regular,
  Cart20Regular,
  ClipboardTask20Regular,
  DocumentBulletList20Regular,
  ErrorCircle16Regular,
} from '@fluentui/react-icons';
import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate } from 'react-router-dom';
import { useApi } from '@/api/ApiProvider';
import { documentLabel, documentLink, getDashboard, type Dashboard as DashboardData } from '@/api/dashboard';
import { errorMessage } from '@/components/notify';
import { PoStatusBadges } from '@/pages/purchaseOrders/PurchaseOrdersPage';
import { formatDate } from '@/pages/purchaseRequisitions/shared';
import { useSessionStore } from '@/state/sessionStore';

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', rowGap: '16px', marginTop: '8px' },
  actions: { display: 'flex', flexWrap: 'wrap', gap: '8px', alignItems: 'center' },
  spacer: { flexGrow: 1 },
  kpis: { display: 'grid', gap: '12px', gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))' },
  kpiLink: { textDecoration: 'none', color: 'inherit', display: 'block' },
  kpi: { padding: '14px 16px', rowGap: '4px', height: '100%', ':hover': { boxShadow: tokens.shadow8 } },
  kpiValue: { fontSize: tokens.fontSizeHero800, lineHeight: tokens.lineHeightHero800, fontWeight: tokens.fontWeightSemibold, fontVariantNumeric: 'tabular-nums' },
  kpiLabel: { color: tokens.colorNeutralForeground2 },
  warn: { color: tokens.colorPaletteDarkOrangeForeground1 },
  twoCol: { display: 'grid', gap: '16px', gridTemplateColumns: '1fr', '@media (min-width: 960px)': { gridTemplateColumns: '3fr 2fr' } },
  card: { padding: '16px 20px', rowGap: '12px' },
  muted: { color: tokens.colorNeutralForeground3 },
  chart: { display: 'flex', alignItems: 'flex-end', gap: '2px', height: '140px', borderBottom: `1px solid ${tokens.colorNeutralStroke2}` },
  barSlot: { flex: '1 1 0', height: '100%', display: 'flex', alignItems: 'flex-end', cursor: 'default', minWidth: 0 },
  bar: {
    width: '100%',
    minHeight: '2px',
    backgroundColor: tokens.colorBrandBackground,
    borderRadius: '4px 4px 0 0',
    transitionProperty: 'background-color',
    transitionDuration: tokens.durationFaster,
  },
  barHover: { backgroundColor: tokens.colorBrandBackgroundHover },
  barEmpty: { backgroundColor: tokens.colorNeutralStroke2 },
  axis: { display: 'flex', gap: '2px', marginTop: '4px' },
  axisLabel: { flex: '1 1 0', textAlign: 'center', fontSize: tokens.fontSizeBase100, color: tokens.colorNeutralForeground3, minWidth: 0 },
  readout: { minHeight: '20px' },
  list: { display: 'flex', flexDirection: 'column', rowGap: '10px', margin: 0, padding: 0, listStyle: 'none' },
  row: { display: 'flex', justifyContent: 'space-between', columnGap: '12px', alignItems: 'baseline' },
  num: { fontVariantNumeric: 'tabular-nums', fontWeight: tokens.fontWeightSemibold },
  mono: { fontFamily: tokens.fontFamilyMonospace },
  activity: { display: 'flex', flexDirection: 'column', rowGap: '2px' },
});

function Kpi({ k }: { k: DashboardData['kpis'][number] }) {
  const s = useStyles();
  const alert = (k.key === 'casesOverdue' || k.key === 'poApproval') && (k.value ?? 0) > 0;
  return (
    <Link to={k.link} className={s.kpiLink} aria-label={`${k.label}: ${k.value ?? 'unavailable'}`}>
    <Card className={s.kpi}>
      {k.value === null ? (
        <Tooltip content={k.error ?? 'Unavailable'} relationship="description">
          <span className={mergeClasses(s.kpiValue, s.muted)}>—</span>
        </Tooltip>
      ) : (
        <span className={mergeClasses(s.kpiValue, alert && s.warn)}>{k.value.toLocaleString()}</span>
      )}
      <Caption1 className={s.kpiLabel}>{k.label}</Caption1>
    </Card>
    </Link>
  );
}

/** Documents created through the portal per day (single series, hover/focus shows the value). */
function CreatedChart({ data }: { data: DashboardData['createdPerDay'] }) {
  const s = useStyles();
  const [hover, setHover] = useState<number | null>(null);
  const max = Math.max(1, ...data.map((d) => d.count));
  const total = data.reduce((a, d) => a + d.count, 0);
  const label = (d: string) => new Date(`${d}T00:00:00`).toLocaleDateString(undefined, { day: 'numeric', month: 'short' });
  const shown = hover !== null ? data[hover] : null;
  return (
    <div>
      <Body1 className={s.readout} aria-live="polite">
        {shown ? <><strong>{shown.count}</strong> created on {label(shown.date)}</> : <><strong>{total}</strong> in the last 14 days</>}
      </Body1>
      <div className={s.chart} role="list" aria-label="Documents created per day">
        {data.map((d, i) => (
          <div key={d.date} className={s.barSlot} role="listitem" tabIndex={0}
            aria-label={`${label(d.date)}: ${d.count}`}
            onMouseEnter={() => setHover(i)} onMouseLeave={() => setHover(null)}
            onFocus={() => setHover(i)} onBlur={() => setHover(null)}>
            <div className={mergeClasses(s.bar, d.count === 0 && s.barEmpty, hover === i && d.count > 0 && s.barHover)}
              style={{ height: `${(d.count / max) * 100}%` }} />
          </div>
        ))}
      </div>
      <div className={s.axis} aria-hidden>
        {data.map((d, i) => <span key={d.date} className={s.axisLabel}>{i % 3 === 0 || i === data.length - 1 ? label(d.date) : ''}</span>)}
      </div>
    </div>
  );
}

export function Dashboard({ isAdmin }: { isAdmin: boolean }) {
  const s = useStyles();
  const api = useApi();
  const navigate = useNavigate();
  const tenantId = useSessionStore((st) => st.tenantId);
  const company = useSessionStore((st) => st.company);
  const q = useQuery({
    queryKey: ['dashboard', tenantId, company],
    queryFn: ({ signal }) => getDashboard(api, signal),
    staleTime: 60_000,
    enabled: !!company,
  });
  const d = q.data;
  const byType = Object.entries(d?.createdByType ?? {}).sort((a, b) => b[1] - a[1]);

  return (
    <section className={s.root} aria-label="Dashboard">
      <div className={s.actions}>
        <Button icon={<ClipboardTask20Regular />} onClick={() => navigate('/purchase-requisitions/new')}>New requisition</Button>
        <Button icon={<Cart20Regular />} onClick={() => navigate('/purchase-orders/new')}>New purchase order</Button>
        <Button icon={<DocumentBulletList20Regular />} onClick={() => navigate('/cases/new')}>New CAPA case</Button>
        <Button icon={<BookOpen20Regular />} onClick={() => navigate('/general-journals/new')}>New journal</Button>
        <span className={s.spacer} />
        <Button appearance="subtle" icon={<ArrowClockwise20Regular />} onClick={() => q.refetch()} disabled={q.isFetching}>
          {q.isFetching ? 'Refreshing…' : 'Refresh'}
        </Button>
      </div>

      {q.isError && <MessageBar intent="error"><MessageBarBody>{errorMessage(q.error)}</MessageBarBody></MessageBar>}
      {d?.error && <MessageBar intent="warning"><MessageBarBody>Live D365 figures are unavailable: {d.error}</MessageBarBody></MessageBar>}

      {q.isLoading ? (
        <Skeleton aria-label="Loading dashboard"><div className={s.kpis}>{Array.from({ length: 6 }, (_, i) => <SkeletonItem key={i} size={72} />)}</div></Skeleton>
      ) : d && (
        <>
          {d.kpis.length > 0 && <div className={s.kpis}>{d.kpis.map((k) => <Kpi key={k.key} k={k} />)}</div>}

          <div className={s.twoCol}>
            <Card className={s.card}>
              <Subtitle2 as="h2">Created through the portal</Subtitle2>
              <CreatedChart data={d.createdPerDay} />
            </Card>
            <Card className={s.card}>
              <Subtitle2 as="h2">Last 30 days</Subtitle2>
              {byType.length === 0 ? <Caption1 className={s.muted}>Nothing created yet.</Caption1> : (
                <ul className={s.list}>
                  {byType.map(([type, n]) => (
                    <li key={type} className={s.row}>
                      <Link to={documentLink[type] ?? '/'}>{documentLabel[type] ?? type}</Link>
                      <span className={s.num}>{n}</span>
                    </li>
                  ))}
                </ul>
              )}
              <div className={s.row}>
                <Caption1 className={s.muted}>
                  <ErrorCircle16Regular style={{ verticalAlign: 'text-bottom' }} /> Failed D365 calls (7 days)
                </Caption1>
                {isAdmin ? <Link to="/admin/audit" className={s.num}>{d.failedD365CallsLast7Days}</Link>
                  : <span className={s.num}>{d.failedD365CallsLast7Days}</span>}
              </div>
            </Card>
          </div>

          <div className={s.twoCol}>
            <Card className={s.card}>
              <div className={s.row}>
                <Subtitle2 as="h2">Recent purchase orders</Subtitle2>
                <Button size="small" appearance="subtle" icon={<Add16Regular />} onClick={() => navigate('/purchase-orders/new')}>New</Button>
              </div>
              {d.recentPurchaseOrders.length === 0 ? <Caption1 className={s.muted}>No purchase orders in {d.company}.</Caption1> : (
                <ul className={s.list}>
                  {d.recentPurchaseOrders.map((p) => (
                    <li key={p.purchaseOrderNumber} className={s.row}>
                      <span>
                        <Link className={s.mono} to={`/purchase-orders/${encodeURIComponent(p.purchaseOrderNumber)}`}>{p.purchaseOrderNumber}</Link>{' '}
                        <span className={s.muted}>{p.vendorName}</span>
                      </span>
                      <PoStatusBadges status={p.status} approval={p.approvalStatus} />
                    </li>
                  ))}
                </ul>
              )}
            </Card>
            <Card className={s.card}>
              <Subtitle2 as="h2">Recent activity</Subtitle2>
              {d.recentActivity.length === 0 ? <Caption1 className={s.muted}>No documents created through the portal yet.</Caption1> : (
                <ul className={s.list}>
                  {d.recentActivity.map((a, i) => (
                    <li key={i} className={s.activity}>
                      <Body1>{documentLabel[a.entity] ?? a.entity}: {a.summary}</Body1>
                      <Caption1 className={s.muted}>
                        {a.userEmail} · {a.company} · {new Date(a.timestamp).toLocaleString()}
                      </Caption1>
                    </li>
                  ))}
                </ul>
              )}
            </Card>
          </div>
          <Caption1 className={s.muted}>
            Live from D365 for company {d.company}. Requisition counts cover all companies (requisitions are shared in D365).
            {d.createdPerDay.length > 0 && ` Updated ${formatDate(new Date().toISOString().slice(0, 10))}.`}
          </Caption1>
        </>
      )}
    </section>
  );
}
