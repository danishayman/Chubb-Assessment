import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Placeholder blocks shown while a page's data loads, so the layout doesn't jump. */
@Component({
  selector: 'app-skeleton',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { 'aria-busy': 'true', 'aria-label': 'Loading' },
  template: `
    @for (r of rowsArr(); track $index) {
      <div class="skeleton" [style.width.%]="$index % 3 === 2 ? 60 : 100" [style.height.px]="height()"></div>
    }
  `,
})
export class Skeleton {
  rows = input(3);
  height = input(16);
  protected rowsArr = () => Array.from({ length: this.rows() });
}
