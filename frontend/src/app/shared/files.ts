/** Client-side mirror of the server's rules, so people find out before uploading. The server still decides. */
export const ACCEPTED_EXTENSIONS = ['.pdf', '.jpg', '.jpeg', '.png', '.webp'];
export const ACCEPT_ATTR = ACCEPTED_EXTENSIONS.join(',');
export const MAX_FILE_BYTES = 10 * 1024 * 1024;

export function fileProblem(file: File): string | null {
  const ext = file.name.slice(file.name.lastIndexOf('.')).toLowerCase();
  if (!ACCEPTED_EXTENSIONS.includes(ext)) return `${file.name}: only PDF, JPG, PNG or WEBP files are accepted.`;
  if (file.size === 0) return `${file.name} is empty.`;
  if (file.size > MAX_FILE_BYTES) return `${file.name} is larger than 10 MB.`;
  return null;
}

export function fileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

export function isImage(contentType: string): boolean {
  return contentType.startsWith('image/');
}
