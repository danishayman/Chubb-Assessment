import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { ApiService, errorMessage } from '../core/api.service';
import { Attachment } from '../core/models';
import { fileSize, isImage } from './files';
import { Icon } from './icon';

@Component({
  selector: 'app-attachment-list',
  imports: [DatePipe, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (items().length) {
      <ul class="attachments">
        @for (a of items(); track a.id) {
          <li>
            <span class="file-icon"><app-icon [name]="image(a.contentType) ? 'image' : 'file'" [size]="18" /></span>
            <div class="file-meta">
              <span class="file-name">{{ a.fileName }}</span>
              <span class="muted small">{{ size(a.sizeBytes) }} · {{ a.uploadedAt | date: 'd MMM y, HH:mm' }}</span>
            </div>
            <button type="button" class="btn small ghost" [disabled]="busy() === a.id" (click)="download(a)">
              <app-icon name="download" [size]="15" /> {{ busy() === a.id ? 'Downloading…' : 'Download' }}
            </button>
          </li>
        }
      </ul>
    } @else if (emptyText()) {
      <p class="muted small">{{ emptyText() }}</p>
    }
    @if (error()) { <div class="field-error">{{ error() }}</div> }
  `,
})
export class AttachmentList {
  private api = inject(ApiService);

  claimId = input.required<string>();
  items = input.required<Attachment[]>();
  emptyText = input('');

  protected busy = signal<string | null>(null);
  protected error = signal('');
  protected size = fileSize;
  protected image = isImage;

  // Downloads need the bearer token, so fetch as a blob rather than linking straight to the URL.
  protected async download(a: Attachment): Promise<void> {
    this.busy.set(a.id);
    this.error.set('');
    try {
      const blob = await this.api.downloadAttachment(this.claimId(), a.id);
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = a.fileName;
      link.click();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(null);
    }
  }
}
