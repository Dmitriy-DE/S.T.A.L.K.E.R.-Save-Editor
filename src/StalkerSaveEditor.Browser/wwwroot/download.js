// Offers bytes written by the editor as a file download.
export function download(fileName, view) {
    const blob = new Blob([view.slice()], { type: "application/octet-stream" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 10_000);
}

export function pageUrl() {
    return globalThis.location.href;
}
