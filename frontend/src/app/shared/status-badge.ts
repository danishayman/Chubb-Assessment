import { Component, computed, input } from '@angular/core';
import { ClaimStatus } from '../core/models';
import { STATUS_LABELS } from './format';

@Component({
  selector: 'app-status',
  template: `<span [class]="'badge s-' + status()">{{ label() }}</span>`,
})
export class StatusBadge {
  status = input.required<ClaimStatus>();
  label = computed(() => STATUS_LABELS[this.status()]);
}
