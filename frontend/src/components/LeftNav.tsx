import { makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { NavLink } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import type { NavItem } from '@/navigation';

const useStyles = makeStyles({
  nav: {
    width: '220px',
    flexShrink: 0,
    backgroundColor: tokens.colorNeutralBackground3,
    borderRight: `1px solid ${tokens.colorNeutralStroke2}`,
    paddingBlock: '8px',
    overflowY: 'auto',
  },
  collapsed: { width: '48px' },
  section: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground3,
    textTransform: 'uppercase',
    letterSpacing: '0.04em',
    padding: '12px 16px 4px',
  },
  link: {
    display: 'flex',
    alignItems: 'center',
    columnGap: '10px',
    height: '36px',
    paddingInline: '14px',
    color: tokens.colorNeutralForeground1,
    textDecoration: 'none',
    fontSize: tokens.fontSizeBase300,
    borderLeft: '3px solid transparent',
    ':hover': { backgroundColor: tokens.colorNeutralBackground3Hover },
    ':focus-visible': { outline: `2px solid ${tokens.colorStrokeFocus2}`, outlineOffset: '-2px' },
  },
  active: {
    backgroundColor: tokens.colorNeutralBackground1,
    borderLeft: `3px solid ${tokens.colorBrandStroke1}`,
    fontWeight: tokens.fontWeightSemibold,
  },
  label: { whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' },
});

const SECTION_LABEL: Record<NavItem['section'], string> = {
  workspaces: 'nav.workspaces',
  administration: 'nav.administration',
  platform: 'nav.platform',
};

export function LeftNav({ items, collapsed }: { items: NavItem[]; collapsed: boolean }) {
  const { t } = useTranslation();
  const s = useStyles();
  const sections = (['workspaces', 'administration', 'platform'] as const)
    .map((section) => ({ section, items: items.filter((i) => i.section === section) }))
    .filter((g) => g.items.length > 0);

  return (
    <nav className={mergeClasses(s.nav, collapsed && s.collapsed)} aria-label="Main">
      {sections.map((g) => (
        <div key={g.section}>
          {!collapsed && <div className={s.section}>{t(SECTION_LABEL[g.section])}</div>}
          {g.items.map((item) => (
            <NavLink
              key={item.path}
              to={item.path}
              end={item.path === '/'}
              title={collapsed ? t(item.labelKey) : undefined}
              className={({ isActive }) => mergeClasses(s.link, isActive && s.active)}
            >
              {item.icon}
              {!collapsed && <span className={s.label}>{t(item.labelKey)}</span>}
            </NavLink>
          ))}
        </div>
      ))}
    </nav>
  );
}
