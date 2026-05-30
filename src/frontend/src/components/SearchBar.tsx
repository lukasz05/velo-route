'use client';

import { useState, useRef, useEffect, useCallback } from 'react';
import type { GeocodingFeature } from '@/types/route';

interface SearchBarProps {
  onSelect: (feature: GeocodingFeature) => void;
  placeholder?: string;
}

export default function SearchBar({ onSelect, placeholder = 'Search for a start location…' }: SearchBarProps) {
  const [query, setQuery] = useState('');
  const [suggestions, setSuggestions] = useState<GeocodingFeature[]>([]);
  const [isOpen, setIsOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(-1);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const abortRef = useRef<AbortController | null>(null);
  const listRef = useRef<HTMLUListElement>(null);

  const fetchSuggestions = useCallback(async (q: string) => {
    if (q.length < 2) { setSuggestions([]); setIsOpen(false); return; }
    abortRef.current?.abort();
    abortRef.current = new AbortController();
    try {
      const res = await fetch(`/api/geocode?q=${encodeURIComponent(q)}`, { signal: abortRef.current.signal });
      const data = await res.json() as { features: GeocodingFeature[] };
      setSuggestions(data.features ?? []);
      setIsOpen((data.features?.length ?? 0) > 0);
      setActiveIndex(-1);
    } catch (err) {
      if (err instanceof Error && err.name === 'AbortError') return;
      setSuggestions([]);
      setIsOpen(false);
    }
  }, []);

  useEffect(() => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => fetchSuggestions(query), 300);
    return () => { if (debounceRef.current) clearTimeout(debounceRef.current); };
  }, [query, fetchSuggestions]);

  useEffect(() => () => { abortRef.current?.abort(); }, []);

  function selectSuggestion(feature: GeocodingFeature) {
    setQuery(feature.properties.label);
    setSuggestions([]);
    setIsOpen(false);
    setActiveIndex(-1);
    onSelect(feature);
  }

  function handleKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    if (!isOpen) return;
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setActiveIndex(i => Math.min(i + 1, suggestions.length - 1));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setActiveIndex(i => Math.max(i - 1, 0));
    } else if (e.key === 'Enter' && activeIndex >= 0) {
      e.preventDefault();
      selectSuggestion(suggestions[activeIndex]);
    } else if (e.key === 'Escape') {
      e.preventDefault();
      setIsOpen(false);
    }
  }

  const listId = 'search-suggestions';

  return (
    <div className="relative">
      <input
        type="text"
        role="combobox"
        aria-expanded={isOpen}
        aria-autocomplete="list"
        aria-controls={listId}
        aria-activedescendant={activeIndex >= 0 ? `suggestion-${activeIndex}` : undefined}
        value={query}
        onChange={e => setQuery(e.target.value)}
        onKeyDown={handleKeyDown}
        onBlur={() => setTimeout(() => setIsOpen(false), 150)}
        placeholder={placeholder}
        className="w-full rounded-lg border border-zinc-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
      />
      {isOpen && (
        <ul
          id={listId}
          ref={listRef}
          role="listbox"
          className="absolute z-10 mt-1 w-full overflow-hidden rounded-lg border border-zinc-200 bg-white shadow-lg"
        >
          {suggestions.map((feature, i) => (
            <li
              key={i}
              id={`suggestion-${i}`}
              role="option"
              aria-selected={i === activeIndex}
              onMouseDown={() => selectSuggestion(feature)}
              className={`cursor-pointer px-3 py-2 text-sm ${i === activeIndex ? 'bg-blue-50 text-blue-700' : 'text-zinc-700 hover:bg-zinc-50'}`}
            >
              {feature.properties.label}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
