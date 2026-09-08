/**
 * One implementation of the comma-separated tag convention, shared by the
 * pre-save panel and the post-save edit form.
 */
export function parseTags(input: string): string[] {
  return input.split(',').map((t) => t.trim()).filter(Boolean);
}

export function formatTags(tags: string[] | null): string {
  return tags ? tags.join(', ') : '';
}
