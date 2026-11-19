import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

// One authored set: 24px grid, 1.75 stroke, round caps. Paths only, so they inherit currentColor.
const PATHS = {
  bell: ['M6 8a6 6 0 1 1 12 0c0 7 3 9 3 9H3s3-2 3-9', 'M10.3 21a1.94 1.94 0 0 0 3.4 0'],
  back: ['m15 18-6-6 6-6'],
  paperclip: ['m21.4 11.05-9.19 9.19a6 6 0 0 1-8.49-8.49l8.57-8.57A4 4 0 1 1 17.95 8.84l-8.6 8.6a2 2 0 0 1-2.83-2.83l8.49-8.48'],
  upload: ['M12 15V3', 'm7 8 5-5 5 5', 'M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4'],
  download: ['M12 3v12', 'm7 10 5 5 5-5', 'M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4'],
  file: ['M14.5 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7.5Z', 'M14 2v6h6'],
  image: ['M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2Z', 'm21 15-5-5L5 21', 'M9 9.5a1.5 1.5 0 1 0 0-.01'],
  x: ['M18 6 6 18', 'm6 6 12 12'],
  plus: ['M12 5v14', 'M5 12h14'],
  alert: ['M12 9v4', 'M12 17h.01', 'M10.3 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.7 3.86a2 2 0 0 0-3.4 0Z'],
  check: ['M20 6 9 17l-5-5'],
} as const;

export type IconName = keyof typeof PATHS;

@Component({
  selector: 'app-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'icon', 'aria-hidden': 'true' },
  template: `
    <svg [attr.width]="size()" [attr.height]="size()" viewBox="0 0 24 24" fill="none" stroke="currentColor"
         stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round">
      @for (d of paths(); track $index) { <path [attr.d]="d" /> }
    </svg>
  `,
})
export class Icon {
  name = input.required<IconName>();
  size = input(18);
  protected paths = computed(() => PATHS[this.name()]);
}
