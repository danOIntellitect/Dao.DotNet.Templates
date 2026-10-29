import { Component, signal, ChangeDetectionStrategy } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { WeatherComponent } from './weather/weather.component';
//#if (IncludeAuthentication)
import { inject } from '@angular/core';
import { AuthService } from './auth/auth.service';
//#endif
//#if (IncludeSignalR)
import { NotificationBadgeComponent } from './signalr/notification-badge.component';
//#endif

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, WeatherComponent,
//#if (IncludeSignalR)
    NotificationBadgeComponent,
//#endif
  ],
  templateUrl: './app.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './app.scss'
})
export class App {
//#if (IncludeAuthentication)
  protected readonly auth = inject(AuthService);
//#endif
  protected readonly title = signal('Dao.Aspire.Angular.Web');
}
