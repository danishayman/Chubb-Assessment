import { ChangeDetectionStrategy, Component, input, model, signal } from '@angular/core';
import { ACCEPT_ATTR, fileProblem, fileSize } from './files';
import { Icon } from './icon';

/** Drop zone plus file input. Holds the chosen files; the parent decides when to upload them. */
@Component({
  selector: 'app-file-picker',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <label class="dropzone" [class.over]="over()" [class.compact]="compact()"
           (dragover)="$event.preventDefault(); over.set(true)" (dragleave)="over.set(false)" (drop)="drop($event)">
      <input type="file" multiple [accept]="accept" [disabled]="disabled()" (change)="pick($event)" />
      <app-icon name="upload" [size]="20" />
      <span><strong>Choose files</strong> or drag them here</span>
      <span class="muted small">PDF, JPG, PNG or WEBP · up to 10 MB each</span>
    </label>

    @for (p of problems(); track p) {
      <div class="field-error"><app-icon name="alert" [size]="14" /> {{ p }}</div>
    }

    @if (files().length) {
      <ul class="file-chips">
        @for (f of files(); track f.name + f.size) {
          <li>
            <app-icon name="file" [size]="16" />
            <span class="file-name">{{ f.name }}</span>
            <span class="muted small">{{ size(f.size) }}</span>
            <button type="button" class="icon-btn" [disabled]="disabled()" (click)="remove(f)" [attr.aria-label]="'Remove ' + f.name">
              <app-icon name="x" [size]="14" />
            </button>
          </li>
        }
      </ul>
    }
  `,
})
export class FilePicker {
  files = model<File[]>([]);
  disabled = input(false);
  compact = input(false);

  protected accept = ACCEPT_ATTR;
  protected over = signal(false);
  protected problems = signal<string[]>([]);
  protected size = fileSize;

  protected pick(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.add(Array.from(input.files ?? []));
    input.value = '';
  }

  protected drop(event: DragEvent): void {
    event.preventDefault();
    this.over.set(false);
    if (!this.disabled()) this.add(Array.from(event.dataTransfer?.files ?? []));
  }

  protected remove(file: File): void {
    this.files.update(list => list.filter(f => f !== file));
  }

  private add(incoming: File[]): void {
    const problems: string[] = [];
    const ok: File[] = [];
    for (const f of incoming) {
      const problem = fileProblem(f);
      if (problem) problems.push(problem);
      else if (!this.files().some(x => x.name === f.name && x.size === f.size)) ok.push(f);
    }
    this.problems.set(problems);
    this.files.update(list => [...list, ...ok]);
  }
}
