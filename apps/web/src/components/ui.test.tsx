import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { CautionPanel } from '@/components/ui';

/**
 * The panel that shows the household's own safety notes on a dose row.
 *
 * Two rules worth pinning. It must render nothing — not an empty box — when there are no
 * notes, because an empty warning-coloured panel on every row would teach the eye to skip
 * the one that has something in it. And it must keep the household's line breaks, because
 * "no citrus / no grapefruit" written on two lines is a list, and running it together
 * loses that.
 */

const NOTES = [
  { label: 'Birlikte almayın', value: 'Kan sulandırıcı' },
  { label: 'Kaçının', value: 'Greyfurt\nPortakal' },
];

describe('CautionPanel', () => {
  it('renders nothing at all when the household wrote nothing', () => {
    const { container } = render(
      <CautionPanel title="Dikkat" ownLabel="Hanenin notu" notes={[]} />,
    );

    // No wrapper, no empty section — nothing. Plain `innerHTML` rather than a jest-dom
    // matcher, so the runner needs no extra package to say it.
    expect(container.innerHTML).toBe('');
  });

  it('is a labelled region a screen reader can jump to', () => {
    render(<CautionPanel title="Dikkat" ownLabel="Hanenin notu" notes={NOTES} />);

    // One landmark per panel, named by the title, so "jump to the next region" lands on
    // the warning rather than on the button after it.
    // getByRole throws when there is no such region, so reaching the assertion is the test.
    expect(screen.getByRole('region', { name: 'Dikkat' })).toBeTruthy();
  });

  it('says plainly that the note is the household\'s own, not the software\'s', () => {
    render(<CautionPanel title="Dikkat" ownLabel="Hanenin notu" notes={NOTES} />);

    // This label is the product boundary made visible: the software never derives a word
    // of these notes, and the panel must not read as if it had.
    expect(screen.getByText('Hanenin notu')).toBeTruthy();
  });

  it('lists every note under its own label, in the order given', () => {
    render(<CautionPanel title="Dikkat" ownLabel="Hanenin notu" notes={NOTES} />);

    const terms = screen.getAllByRole('term').map((element) => element.textContent);

    expect(terms).toEqual(['Birlikte almayın', 'Kaçının']);
    expect(screen.getByText('Kan sulandırıcı')).toBeTruthy();
  });

  it('keeps the household\'s own line breaks', () => {
    render(<CautionPanel title="Dikkat" ownLabel="Hanenin notu" notes={NOTES} />);

    const value = screen.getByText((_, element) => element?.textContent === 'Greyfurt\nPortakal');

    // The text keeps its newline, and the element is styled to honour it. Either half on
    // its own would run the two lines together.
    expect(value.textContent).toBe('Greyfurt\nPortakal');
    expect(value.className).toContain('whitespace-pre-line');
  });
});
