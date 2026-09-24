window.nucleoNotifications = {
    isSupported: function () {
        return 'Notification' in window;
    },
    getPermission: function () {
        if (!('Notification' in window)) return 'unsupported';
        return Notification.permission;
    },
    requestPermission: async function () {
        if (!('Notification' in window)) return 'unsupported';
        try {
            const result = await Notification.requestPermission();
            return result;
        } catch (e) {
            console.error('Error requesting notification permission:', e);
            return 'denied';
        }
    },
    showNotification: function (title, body, icon, url) {
        if (!('Notification' in window) || Notification.permission !== 'granted') {
            return false;
        }
        const options = {
            body: body || '',
            icon: icon || '/favicon.png',
            badge: '/favicon.png',
            vibrate: [100, 50, 100],
            data: { url: url || '/notifications' }
        };

        if ('serviceWorker' in navigator && navigator.serviceWorker.controller) {
            navigator.serviceWorker.ready.then(function (registration) {
                registration.showNotification(title, options);
            }).catch(function () {
                const n = new Notification(title, options);
                n.onclick = function () {
                    window.focus();
                    if (options.data && options.data.url) {
                        window.location.href = options.data.url;
                    }
                };
            });
        } else {
            const n = new Notification(title, options);
            n.onclick = function () {
                window.focus();
                if (options.data && options.data.url) {
                    window.location.href = options.data.url;
                }
            };
        }
        return true;
    }
};

window.nucleoSounds = {
    // O som vem de ConnectorPresentation.SoundUrl, declarado pelo plugin.
    // Nenhum marketplace e reconhecido pelo nome aqui.
    playConnector: function (soundUrl) {
        if (soundUrl) {
            return window.nucleoSounds.playUrl(soundUrl);
        }
        return window.nucleoSounds.playBell();
    },
    playUrl: function (url) {
        const audio = new Audio(url);
        audio.volume = 0.55;
        return audio.play().catch(function () { return false; });
    },
    playBell: function () {
        const AudioContext = window.AudioContext || window.webkitAudioContext;
        if (!AudioContext) return Promise.resolve(false);
        const context = new AudioContext();
        const now = context.currentTime;
        [880, 1174.66].forEach(function (frequency, index) {
            const oscillator = context.createOscillator();
            const gain = context.createGain();
            oscillator.type = 'sine';
            oscillator.frequency.value = frequency;
            gain.gain.setValueAtTime(0.0001, now + index * 0.12);
            gain.gain.exponentialRampToValueAtTime(0.18, now + index * 0.12 + 0.02);
            gain.gain.exponentialRampToValueAtTime(0.0001, now + index * 0.12 + 0.65);
            oscillator.connect(gain).connect(context.destination);
            oscillator.start(now + index * 0.12);
            oscillator.stop(now + index * 0.12 + 0.7);
        });
        return Promise.resolve(true);
    }
};

window.nucleoOAuth = {
    open: function (url) {
        window.open(url, '_blank');
    },
    register: function () {
        if (window.nucleoOAuthRegistered) return;
        window.nucleoOAuthRegistered = true;
        window.addEventListener('message', function (event) {
            if (event.origin !== window.location.origin || !event.data || event.data.source !== 'avallo-oauth') return;
            window.location.href = event.data.success
                ? '/connectors?connected=true'
                : '/connectors?oauthError=' + encodeURIComponent(event.data.error || 'oauth_failed');
        });
    }
};

window.nucleoDeployment = {
    prepareForRestart: function (notice) {
        const detail = {
            noticeId: notice.noticeId,
            version: notice.version,
            message: notice.message,
            restartAtUtc: notice.restartAtUtc
        };
        sessionStorage.setItem('nucleo.deployment.notice', JSON.stringify(detail));
        window.dispatchEvent(new CustomEvent('nucleo:deployment', { detail: detail }));
    }
};
