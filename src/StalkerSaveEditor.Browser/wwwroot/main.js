import { dotnet } from './_framework/dotnet.js'

// Drop the loading text once Avalonia has put its canvas on the page.
const host = document.getElementById('out');
new MutationObserver((_, observer) => {
    if (host.querySelector('canvas')) {
        host.querySelector('.splash')?.remove();
        observer.disconnect();
    }
}).observe(host, { childList: true });

const runtime = await dotnet.withApplicationArgumentsFromQuery().create();
await runtime.runMain();
