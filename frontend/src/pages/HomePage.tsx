import { Body1, Caption1, Card, CardHeader, Subtitle2, Title2, makeStyles, tokens } from '@fluentui/react-components';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import type { NavItem } from '@/navigation';
import { ReferenceDataCard } from '@/components/ReferenceDataCard';

const useStyles = makeStyles({
  header: { marginBottom: '20px', display: 'flex', flexDirection: 'column', rowGap: '4px' },
  grid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))',
    gap: '16px',
  },
  tile: { height: '100%', ':hover': { boxShadow: tokens.shadow8 } },
  tileLink: { textDecoration: 'none', color: 'inherit' },
  icon: { color: tokens.colorBrandForeground1 },
});

export function HomePage({
  userName,
  tenantName,
  company,
  tiles,
}: {
  userName: string;
  tenantName: string | null;
  company: string | null;
  tiles: NavItem[];
}) {
  const { t } = useTranslation();
  const s = useStyles();
  return (
    <>
      <div className={s.header}>
        <Title2 as="h1">{t('home.greeting', { name: userName })}</Title2>
        {tenantName && (
          <Body1>
            {company ? t('home.subtitle', { tenant: tenantName, company }) : t('home.subtitleNoCompany', { tenant: tenantName })}
          </Body1>
        )}
      </div>
      <div className={s.grid}>
        {tiles.map((tile) => (
          <Link key={tile.path} to={tile.path} className={s.tileLink}>
            <Card className={s.tile}>
              <CardHeader
                image={<span className={s.icon}>{tile.icon}</span>}
                header={<Subtitle2>{t(tile.labelKey)}</Subtitle2>}
                description={<Caption1>{t('home.tileOpen')}</Caption1>}
              />
            </Card>
          </Link>
        ))}
      </div>
      {tenantName && company && <ReferenceDataCard company={company} />}
    </>
  );
}
