import {
  Avatar,
  Button,
  Menu,
  MenuDivider,
  MenuGroupHeader,
  MenuItem,
  MenuItemRadio,
  MenuList,
  MenuPopover,
  MenuTrigger,
  Tooltip,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { ArrowSwap20Regular, Building20Regular, ChevronDown16Regular, Navigation20Regular, SignOut20Regular } from '@fluentui/react-icons';
import { useTranslation } from 'react-i18next';

/** F&O-style navigation bar colors. */
export const NAV_BAR_BG = '#002050';

const useStyles = makeStyles({
  bar: {
    height: '48px',
    display: 'flex',
    alignItems: 'center',
    columnGap: '12px',
    paddingInline: '8px 16px',
    backgroundColor: NAV_BAR_BG,
    color: '#ffffff',
    flexShrink: 0,
  },
  title: {
    fontSize: tokens.fontSizeBase400,
    fontWeight: tokens.fontWeightSemibold,
    whiteSpace: 'nowrap',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
  },
  tenant: {
    fontSize: tokens.fontSizeBase300,
    opacity: 0.8,
    whiteSpace: 'nowrap',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    '@media (max-width: 640px)': { display: 'none' },
  },
  spacer: { flexGrow: 1 },
  onDark: {
    color: '#ffffff',
    ':hover': { color: '#ffffff', backgroundColor: 'rgba(255,255,255,0.12)' },
    ':hover:active': { color: '#ffffff', backgroundColor: 'rgba(255,255,255,0.2)' },
  },
  company: {
    fontFamily: tokens.fontFamilyMonospace,
    fontWeight: tokens.fontWeightSemibold,
    letterSpacing: '0.04em',
  },
  chevron: { marginInlineStart: '4px', verticalAlign: 'middle' },
  identity: { display: 'flex', flexDirection: 'column', paddingBlock: '4px' },
  muted: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
});

export interface TopBarProps {
  userName: string;
  email: string | null;
  tenantName: string | null;
  company: string | null;
  /** Companies the user may work in inside the current tenant. */
  companies: string[];
  onSwitchCompany: (company: string) => void;
  switchingCompany?: boolean;
  canSwitchTenant: boolean;
  onSwitchTenant: () => void;
  onSignOut: () => void;
  onToggleNav: () => void;
}

export function TopBar(p: TopBarProps) {
  const { t } = useTranslation();
  const s = useStyles();
  return (
    <header className={s.bar}>
      <Tooltip content="Menu" relationship="label">
        <Button appearance="transparent" className={s.onDark} icon={<Navigation20Regular />} onClick={p.onToggleNav} />
      </Tooltip>
      <span className={s.title}>{t('app.title')}</span>
      {p.tenantName && <span className={s.tenant}>| {p.tenantName}</span>}
      <span className={s.spacer} />

      {p.tenantName && (
        <Menu
          positioning="below-end"
          checkedValues={{ company: p.company ? [p.company] : [] }}
          onCheckedValueChange={(_, d) => {
            const next = d.checkedItems[0];
            if (next && next !== p.company) p.onSwitchCompany(next);
          }}
        >
          <MenuTrigger disableButtonEnhancement>
            <Tooltip content={t('topBar.switchCompany')} relationship="description">
              <Button
                appearance="transparent"
                className={`${s.onDark} ${s.company}`}
                icon={<Building20Regular />}
                iconPosition="before"
                disabled={p.companies.length === 0 || p.switchingCompany}
                aria-label={`${t('topBar.company')}: ${p.company ?? t('topBar.noCompany')}`}
              >
                {p.company ?? t('topBar.noCompany')}
                {p.companies.length > 1 && <ChevronDown16Regular className={s.chevron} />}
              </Button>
            </Tooltip>
          </MenuTrigger>
          <MenuPopover>
            <MenuList>
              <MenuGroupHeader>{t('topBar.companies')}</MenuGroupHeader>
              {p.companies.map((c) => (
                <MenuItemRadio key={c} name="company" value={c}>
                  <span className={s.company}>{c}</span>
                </MenuItemRadio>
              ))}
            </MenuList>
          </MenuPopover>
        </Menu>
      )}

      <Menu positioning="below-end">
        <MenuTrigger disableButtonEnhancement>
          <Button appearance="transparent" className={s.onDark} aria-label={p.userName}>
            <Avatar name={p.userName} size={28} color="colorful" />
          </Button>
        </MenuTrigger>
        <MenuPopover>
          <MenuList>
            <MenuGroupHeader>
              <div className={s.identity}>
                <strong>{p.userName}</strong>
                {p.email && <span className={s.muted}>{p.email}</span>}
                {p.tenantName && (
                  <span className={s.muted}>
                    {t('topBar.tenant')}: {p.tenantName}
                  </span>
                )}
              </div>
            </MenuGroupHeader>
            <MenuDivider />
            {p.canSwitchTenant && (
              <MenuItem icon={<ArrowSwap20Regular />} onClick={p.onSwitchTenant}>
                {t('topBar.switchTenant')}
              </MenuItem>
            )}
            <MenuItem icon={<SignOut20Regular />} onClick={p.onSignOut}>
              {t('topBar.signOut')}
            </MenuItem>
          </MenuList>
        </MenuPopover>
      </Menu>
    </header>
  );
}
