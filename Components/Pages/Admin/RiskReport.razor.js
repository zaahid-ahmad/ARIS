// Holds the one PDF currently previewed. "Download" saves this exact blob, so the file always equals what was shown.
let currentUrl = null;
let currentHash = '';

function revoke() {
    if (currentUrl) {
        URL.revokeObjectURL(currentUrl);
        currentUrl = null;
    }
}

export async function showPdf(iframe, streamRef) {
    const buffer = await streamRef.arrayBuffer();
    revoke();
    currentUrl = URL.createObjectURL(new Blob([buffer], { type: 'application/pdf' }));
    iframe.src = currentUrl;

    // Short fingerprint shown next to the preview so preview and download can be proven identical.
    currentHash = '';
    try {
        const digest = await crypto.subtle.digest('SHA-256', buffer);
        currentHash = Array.from(new Uint8Array(digest)).map(b => b.toString(16).padStart(2, '0')).join('');
    } catch {
        // crypto.subtle is unavailable outside a secure context; the fingerprint is informational only.
    }
    return currentHash;
}

export function downloadPdf(fileName) {
    if (!currentUrl) return;
    const link = document.createElement('a');
    link.href = currentUrl;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
}

export function dispose() {
    revoke();
}
