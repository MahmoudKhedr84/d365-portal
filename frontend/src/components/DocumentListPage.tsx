import { useState, type ReactNode } from 'react';
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
import { useNavigate } from 'react-router-dom';
import { useDebounced } from '@/components/LookupCombobox';
import { errorMessage } from '@/components/notify';
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
  table: { minWidth: '760px' },
  mono: { fontFamily: tokens.fontFamilyMonospace },
  muted: { color: tokens.colorNeutralForeground3 },
  pager: { display: 'flex', alignItems: 'center', columnGap: '8px', justifyContent: 'flex-end' },
});

export interface ListFilter {
  key: string;
  label: string;
  allLabel: string;
  options: { value: string; label: string }[];
}

export interface ListColumn<T> {
  header: string;
  render: (row: T) => ReactNode;
  className?: 'mono' | 'num';
}

export interface DocumentListPageProps<T> {
  title: string;
  queryKey: string;
  searchPlaceholder: string;
  newLabel: string;
  newPath: string;
  emptyText: string;
  filters?: ListFilter[];
  columns: ListColumn<T>[];
  rowKey: (row: T) => string;
  fetch: (p: { search: string; top: number; skip: number; filters: Record<string, string> }, signal?: AbortSignal) =>
    Promise<{ items: T[]; total: number | null }>;
}

/** List page shared by the document modules: search, filters, paging, company badge and "New" button. */
export function DocumentListPage<T>(p: DocumentListPageProps<T>) {
  const s = useStyles();
  const navigate = useNavigate();
  const tenantId = useSessionStore((st) => st.tenantId);
  const company = useSessionStore((st) => st.company);
  const [search, setSearch] = useState('');
  const [filterValues, setFilterValues] = useState<Record<string, string>>({});
  const [skip, setSkip] = useState(0);
  const q = useDebounced(search);

  const list = useQuery({
    queryKey: [p.queryKey, tenantId, company, 'list', q, filterValues, skip],
    queryFn: ({ signal }) => p.fetch({ search: q, top: PAGE, skip, filters: filterValues }, signal),
    placeholderData: keepPreviousData,
    enabled: !!company,
  });
  const total = list.data?.total ?? null;

  return (
    <div className={s.page}>
      <div className={s.head}>
        <Title2 as="h1">{p.title}</Title2>
        <Badge appearance="tint" className={s.mono}>{company ?? '—'}</Badge>
        <span className={s.spacer} />
        <Button appearance="primary" icon={<Add20Regular />} onClick={() => navigate(p.newPath)} disabled={!company}>
          {p.newLabel}
        </Button>
      </div>
      <Card className={s.card}>
        <div className={s.toolbar}>
          <Input className={s.search} contentBefore={<Search20Regular />} placeholder={p.searchPlaceholder} value={search}
            aria-label={p.searchPlaceholder} onChange={(_, d) => { setSearch(d.value); setSkip(0); }} />
          {p.filters?.map((f) => {
            const value = filterValues[f.key] ?? '';
            return (
              <Dropdown key={f.key} aria-label={f.label} value={f.options.find((o) => o.value === value)?.label ?? f.allLabel}
                selectedOptions={[value]}
                onOptionSelect={(_, d) => { setFilterValues((v) => ({ ...v, [f.key]: d.optionValue ?? '' })); setSkip(0); }}>
                <Option value="">{f.allLabel}</Option>
                {f.options.map((o) => <Option key={o.value} value={o.value}>{o.label}</Option>)}
              </Dropdown>
            );
          })}
        </div>
        {!company ? (
          <Caption1 className={s.muted}>Select a company in the top bar.</Caption1>
        ) : list.isError ? (
          <MessageBar intent="error"><MessageBarBody>{errorMessage(list.error)}</MessageBarBody></MessageBar>
        ) : list.isLoading ? (
          <Spinner label="Loading from D365…" />
        ) : (
          <>
            <div className={s.scroll}>
              <Table aria-label={p.title} size="small" className={s.table}>
                <TableHeader>
                  <TableRow>{p.columns.map((c) => <TableHeaderCell key={c.header}>{c.header}</TableHeaderCell>)}</TableRow>
                </TableHeader>
                <TableBody>
                  {list.data!.items.length === 0 && (
                    <TableRow><TableCell colSpan={p.columns.length}><Caption1 className={s.muted}>{p.emptyText}</Caption1></TableCell></TableRow>
                  )}
                  {list.data!.items.map((row) => (
                    <TableRow key={p.rowKey(row)}>
                      {p.columns.map((c) => (
                        <TableCell key={c.header} className={c.className === 'mono' ? s.mono : undefined}
                          style={c.className === 'num' ? { textAlign: 'end', fontVariantNumeric: 'tabular-nums' } : undefined}>
                          {c.render(row)}
                        </TableCell>
                      ))}
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
