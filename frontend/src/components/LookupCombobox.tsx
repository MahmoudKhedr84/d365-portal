import { useEffect, useState } from 'react';
import { Combobox, Field, Option, Spinner, makeStyles, tokens } from '@fluentui/react-components';
import { useQuery } from '@tanstack/react-query';
import { useApi } from '@/api/ApiProvider';
import { searchLookup, type LookupItem } from '@/api/lookups';
import { errorMessage } from '@/components/notify';
import { useSessionStore } from '@/state/sessionStore';

const useStyles = makeStyles({
  id: { fontFamily: tokens.fontFamilyMonospace, marginInlineEnd: '8px' },
  name: { color: tokens.colorNeutralForeground3 },
  note: { color: tokens.colorPaletteRedForeground1, fontSize: tokens.fontSizeBase200 },
});

export function useDebounced<T>(value: T, ms = 300): T {
  const [v, setV] = useState(value);
  useEffect(() => {
    const h = setTimeout(() => setV(value), ms);
    return () => clearTimeout(h);
  }, [value, ms]);
  return v;
}

export interface LookupComboboxProps {
  lookup: string;
  label: string;
  value: string | null;
  onChange: (id: string | null, item: LookupItem | null) => void;
  /** Extra server-side filters, e.g. { site: '1' } for warehouses. */
  filters?: Record<string, string | undefined>;
  required?: boolean;
  disabled?: boolean;
  placeholder?: string;
  validationMessage?: string;
  /** Options for which this returns a note are shown disabled with the note (e.g. "On hold"). */
  disabledNote?: (item: LookupItem) => string | null;
}

/**
 * Searchable lookup backed by live D365 data (/api/lookups/{key}). Results are scoped to the
 * selected tenant and company, so switching company re-queries automatically.
 */
export function LookupCombobox(p: LookupComboboxProps) {
  const s = useStyles();
  const api = useApi();
  const tenantId = useSessionStore((st) => st.tenantId);
  const company = useSessionStore((st) => st.company);
  const [text, setText] = useState(p.value ?? '');
  const [open, setOpen] = useState(false);
  const search = useDebounced(text);

  useEffect(() => setText(p.value ?? ''), [p.value]);

  const query = useQuery({
    queryKey: ['lookups', tenantId, company, p.lookup, p.filters ?? {}, search === p.value ? '' : search],
    queryFn: ({ signal }) => searchLookup(api, p.lookup, search === p.value ? '' : search, p.filters, signal),
    enabled: open && !p.disabled,
    staleTime: 60_000,
  });

  const items = query.data ?? [];
  return (
    <Field label={p.label} required={p.required} validationMessage={p.validationMessage ?? (query.isError ? errorMessage(query.error) : undefined)}>
      <Combobox
        freeform
        disabled={p.disabled}
        placeholder={p.placeholder ?? 'Type to search'}
        value={text}
        selectedOptions={p.value ? [p.value] : []}
        onOpenChange={(_, d) => setOpen(d.open)}
        onChange={(e) => {
          setText(e.target.value);
          setOpen(true);
          if (!e.target.value) p.onChange(null, null);
        }}
        onOptionSelect={(_, d) => {
          const item = items.find((i) => i.id === d.optionValue) ?? null;
          setText(d.optionValue ?? '');
          p.onChange(d.optionValue ?? null, item);
        }}
      >
        {query.isFetching && items.length === 0 ? (
          <Option disabled value="__loading" text="Loading">
            <Spinner size="tiny" label="Loading…" />
          </Option>
        ) : items.length === 0 ? (
          <Option disabled value="__empty" text="No results">
            {query.isError ? 'Could not load' : 'No results'}
          </Option>
        ) : (
          items.map((i) => {
            const note = p.disabledNote?.(i) ?? null;
            return (
              <Option key={i.id} value={i.id} text={i.id} disabled={!!note}>
                <span className={s.id}>{i.id}</span>
                <span className={s.name}>{i.name}</span>
                {note && <span className={s.note}> · {note}</span>}
              </Option>
            );
          })
        )}
      </Combobox>
    </Field>
  );
}
