window.nucleo = window.nucleo || {};

window.nucleo.setLanguage = language => document.documentElement.lang = language;

window.nucleo.downloadFile = (fileName, contentType, base64Content) => {
    const binary = atob(base64Content);
    const bytes = new Uint8Array(binary.length);
    for (let index = 0; index < binary.length; index++)
        bytes[index] = binary.charCodeAt(index);

    const url = URL.createObjectURL(new Blob([bytes], { type: contentType }));
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = fileName;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
};
