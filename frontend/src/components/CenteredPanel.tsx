import type { ReactNode } from 'react';
import { Card, Title2, makeStyles, tokens } from '@fluentui/react-components';

const useStyles = makeStyles({
  page: {
    minHeight: '100vh',
    display: 'grid',
    placeItems: 'center',
    padding: '16px',
    boxSizing: 'border-box',
    backgroundColor: tokens.colorNeutralBackground2,
  },
  card: {
    width: '100%',
    maxWidth: '460px',
    padding: '28px',
    rowGap: '16px',
  },
  brand: {
    display: 'flex',
    alignItems: 'center',
    columnGap: '10px',
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
  },
});

export function CenteredPanel({ title, children }: { title: string; children: ReactNode }) {
  const s = useStyles();
  return (
    <main className={s.page}>
      <Card className={s.card}>
        <div className={s.brand}>
          <img src="/favicon.svg" alt="" width={24} height={24} />
          D365 Portal
        </div>
        <Title2 as="h1">{title}</Title2>
        {children}
      </Card>
    </main>
  );
}
