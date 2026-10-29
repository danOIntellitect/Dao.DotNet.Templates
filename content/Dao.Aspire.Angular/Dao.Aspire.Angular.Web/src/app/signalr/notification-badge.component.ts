import { Component, inject, ChangeDetectionStrategy } from '@angular/core';
import { SignalrService } from './signalr.service';

@Component({
  selector: 'app-notification-badge',
  standalone: true,
  template: `
    <aside class="notification-badge">
      <span class="badge-label">Live Notifications</span>
//#if (IncludeAuthentication)
      @if (service.error()) {
        <p role="alert">{{ service.error() }}</p>
      }
//#endif
      @if (service.notifications().length > 0) {
        <span class="badge-count">{{ service.notifications().length }}</span>
      }
      @if (service.notifications().length > 0) {
        <ul class="notification-list">
          @for (msg of service.notifications(); track msg) {
            <li>{{ msg }}</li>
          }
        </ul>
      } @else {
        <p class="badge-empty">No notifications yet — POST to <code>/api/notify?message=hello</code></p>
      }
    </aside>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    .notification-badge {
      position: fixed;
      bottom: 1.5rem;
      right: 1.5rem;
      background: #fff;
      border: 1px solid #e0e0e0;
      border-radius: 0.75rem;
      padding: 0.75rem 1rem;
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
      min-width: 220px;
      max-width: 320px;
      font-size: 0.875rem;
    }

    .badge-label {
      font-weight: 600;
      color: var(--accent, #058743);
    }

    .badge-count {
      background: var(--accent, #058743);
      color: #fff;
      border-radius: 999px;
      padding: 0.1rem 0.5rem;
      font-size: 0.75rem;
      margin-left: 0.375rem;
    }

    .notification-list {
      margin: 0.5rem 0 0;
      padding-left: 1.25rem;
      list-style: disc;
      color: #333;
    }

    .badge-empty {
      margin: 0.5rem 0 0;
      color: #999;
      font-size: 0.8rem;
    }

    .badge-empty code {
      background: #f4f4f4;
      padding: 0.1rem 0.3rem;
      border-radius: 0.2rem;
      font-size: 0.75rem;
    }
  `,
})
export class NotificationBadgeComponent {
  readonly service = inject(SignalrService);
}
