import { useState } from 'react';
import { Caption1, Card, Dropdown, Field, Option, Subtitle2, makeStyles, tokens } from '@fluentui/react-components';
import { DatabaseSearch20Regular } from '@fluentui/react-icons';
import { useQuery } from '@tanstack/react-query';
import { useApi } from '@/api/ApiProvider';
import { listLookups, type LookupItem } from '@/api/lookups';
import { LookupCombobox } from './LookupCombobox';

const useStyles = makeStyles({
  card: { padding: '20px', rowGap: '12px', maxWidth: '760px', marginTop: '24px' },
  header: { display: 'flex', alignItems: 'center', columnGap: '8px' },
  grid: {
    display: 'grid',
    gap: '12px 16px',
    gridTemplateColumns: '1fr',
    '@media (min-width: 640px)': { gridTemplateColumns: '220px 1fr' },
  },
  muted: { color: tokens.colorNeutralForeground3 },
  detail: { fontFamily: tokens.fontFamilyMonospace, fontSize: tokens.fontSizeBase200, color: tokens.colorNeutralForeground2 },
});

/** Browse live D365 reference data for the current company — a quick way to verify the connection and company switch. */
export function ReferenceDataCard({ company }: { company: string | null }) {
  const s = useStyles();
  const api = useApi();
  const lookups = useQuery({ queryKey: ['lookups', 'catalog'], queryFn: () => listLookups(api), staleTime: Infinity });
  const [key, setKey] = useState('vendors');
  const [value, setValue] = useState<string | null>(null);
  const [item, setItem] = useState<LookupItem | null>(null);
  const label = lookups.data?.find((l) => l.key === key)?.label ?? key;

  return (
    <Card className={s.card}>
      <div className={s.header}>
        <DatabaseSearch20Regular />
        <Subtitle2 as="h2">D365 reference data</Subtitle2>
      </div>
      <Caption1 className={s.muted}>
        Live from D365{company ? ` for company ${company}` : ''}. Switch company in the top bar to see that company’s data.
      </Caption1>
      <div className={s.grid}>
        <Field label="Lookup">
          <Dropdown
            value={label}
            selectedOptions={[key]}
            onOptionSelect={(_, d) => {
              setKey(d.optionValue ?? 'vendors');
              setValue(null);
              setItem(null);
            }}
          >
            {(lookups.data ?? [{ key: 'vendors', label: 'Vendors' }]).map((l) => (
              <Option key={l.key} value={l.key}>
                {l.label}
              </Option>
            ))}
          </Dropdown>
        </Field>
        <LookupCombobox
          key={key}
          lookup={key}
          label={label}
          value={value}
          onChange={(id, it) => {
            setValue(id);
            setItem(it);
          }}
        />
      </div>
      {item && (
        <div className={s.detail}>
          {item.id} · {item.name}
          {item.extra &&
            Object.entries(item.extra)
              .filter(([, v]) => v)
              .map(([k, v]) => ` · ${k}: ${v}`)
              .join('')}
        </div>
      )}
    </Card>
  );
}
