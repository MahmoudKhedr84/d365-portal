import type { ReactNode } from 'react';
import {
  Body1,
  Breadcrumb,
  BreadcrumbButton,
  BreadcrumbDivider,
  BreadcrumbItem,
  Button,
  Caption1,
  MessageBar,
  MessageBarBody,
  Spinner,
  Title2,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { Open20Regular } from '@fluentui/react-icons';
import { useNavigate } from 'react-router-dom';
import { errorMessage } from '@/components/notify';

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', rowGap: '16px' },
  head: { display: 'flex', alignItems: 'center', columnGap: '12px', flexWrap: 'wrap', rowGap: '8px' },
  spacer: { flexGrow: 1 },
  facts: { display: 'grid', gap: '12px 24px', gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))' },
  fact: { display: 'flex', flexDirection: 'column', rowGap: '2px' },
  label: { color: tokens.colorNeutralForeground3 },
  value: { whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' },
});

export function Crumbs({ parent, parentPath, current }: { parent: string; parentPath: string; current: string }) {
  const navigate = useNavigate();
  return (
    <Breadcrumb aria-label="Breadcrumb">
      <BreadcrumbItem><BreadcrumbButton onClick={() => navigate(parentPath)}>{parent}</BreadcrumbButton></BreadcrumbItem>
      <BreadcrumbDivider />
      <BreadcrumbItem><BreadcrumbButton current>{current}</BreadcrumbButton></BreadcrumbItem>
    </Breadcrumb>
  );
}

/** Loading / error frame for a detail page. */
export function DetailFrame({ crumbs, loading, error, loadingLabel, children }: {
  crumbs: ReactNode; loading: boolean; error: unknown; loadingLabel: string; children?: ReactNode;
}) {
  const s = useStyles();
  return (
    <div className={s.page}>
      {crumbs}
      {loading ? <Spinner label={loadingLabel} />
        : error ? <MessageBar intent="error"><MessageBarBody>{errorMessage(error)}</MessageBarBody></MessageBar>
        : children}
    </div>
  );
}

export function DetailHeader({ title, badges, d365Url }: { title: string; badges?: ReactNode; d365Url: string }) {
  const s = useStyles();
  return (
    <div className={s.head}>
      <Title2 as="h1">{title}</Title2>
      {badges}
      <span className={s.spacer} />
      <Button as="a" href={d365Url} target="_blank" rel="noopener noreferrer" icon={<Open20Regular />}>Open in D365</Button>
    </div>
  );
}

export function Facts({ items }: { items: [string, ReactNode][] }) {
  const s = useStyles();
  return (
    <div className={s.facts}>
      {items.map(([label, value]) => (
        <div key={label} className={s.fact}>
          <Caption1 className={s.label}>{label}</Caption1>
          <Body1 className={s.value}>{value === null || value === undefined || value === '' ? '—' : value}</Body1>
        </div>
      ))}
    </div>
  );
}
