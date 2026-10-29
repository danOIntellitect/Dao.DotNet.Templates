import { Injectable, signal, OnDestroy } from '@angular/core';
import * as signalR from '@microsoft/signalr';
//#if (IncludeAuthentication)
import { effect, inject } from '@angular/core';
import { AuthService } from '../auth/auth.service';
//#endif

@Injectable({ providedIn: 'root' })
export class SignalrService implements OnDestroy {
  readonly notifications = signal<string[]>([]);
//#if (IncludeAuthentication)
  readonly error = signal<string | null>(null);
  private readonly auth = inject(AuthService);
  private connection: signalR.HubConnection | undefined;

  constructor() {
    effect((onCleanup) => {
      if (!this.auth.user()) return;
      let disposed = false;
      const connect = async () => {
//#if (GoogleCookie)
        const token = await this.auth.csrfToken();
//#endif
        if (disposed) return;
        const connection = new signalR.HubConnectionBuilder()
          .withUrl('/hubs/notifications', {
//#if (GoogleCookie)
            headers: { 'X-XSRF-TOKEN': token },
//#else
            accessTokenFactory: () => this.auth.accessToken(),
//#endif
          })
          .withAutomaticReconnect()
          .build();
        this.connection = connection;
        connection.on('ReceiveNotification', (message: string) => {
          this.notifications.update(messages => [...messages, message]);
        });
        connection.onclose(error => {
          if (error) this.error.set('Notification connection closed. Sign in again or reload.');
        });
        await connection.start();
        if (disposed) await connection.stop();
      };
      void connect().catch(error => {
        console.error('SignalR connection error:', error);
        this.error.set('Unable to connect to notifications. Check your session and reload.');
      });
      onCleanup(() => {
        disposed = true;
        this.notifications.set([]);
        void this.connection?.stop();
      });
    });
  }

  ngOnDestroy(): void { void this.connection?.stop(); }
//#else
  private readonly connection = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/notifications')
    .withAutomaticReconnect()
    .build();

  constructor() {
    this.connection.on('ReceiveNotification', (message: string) => {
      this.notifications.update((msgs) => [...msgs, message]);
    });

    this.connection
      .start()
      .catch((err) => console.error('SignalR connection error:', err));
  }

  ngOnDestroy(): void {
    this.connection.stop();
  }
//#endif
}
