window.basketEloSocial = {
    copyText: async function (text) {
        await navigator.clipboard.writeText(text);
    },

    downloadSvgAsPng: async function (elementId, fileName) {
        const source = document.getElementById(elementId);
        if (!source) {
            throw new Error(`Social card '${elementId}' was not found.`);
        }

        const clone = source.cloneNode(true);
        clone.setAttribute("width", "1200");
        clone.setAttribute("height", "675");
        clone.setAttribute("xmlns", "http://www.w3.org/2000/svg");

        const serialized = new XMLSerializer().serializeToString(clone);
        const blob = new Blob([serialized], { type: "image/svg+xml;charset=utf-8" });
        const objectUrl = URL.createObjectURL(blob);

        try {
            const image = await new Promise((resolve, reject) => {
                const candidate = new Image();
                candidate.onload = () => resolve(candidate);
                candidate.onerror = () => reject(new Error("The social card could not be rendered."));
                candidate.src = objectUrl;
            });

            const canvas = document.createElement("canvas");
            canvas.width = 1200;
            canvas.height = 675;
            const context = canvas.getContext("2d");
            if (!context) {
                throw new Error("Canvas rendering is not available in this browser.");
            }

            context.drawImage(image, 0, 0, canvas.width, canvas.height);
            const pngBlob = await new Promise((resolve, reject) => {
                canvas.toBlob(result => result ? resolve(result) : reject(new Error("The PNG could not be created.")), "image/png", 1);
            });

            const downloadUrl = URL.createObjectURL(pngBlob);
            try {
                const anchor = document.createElement("a");
                anchor.href = downloadUrl;
                anchor.download = fileName || "basketelo-social-card.png";
                document.body.appendChild(anchor);
                anchor.click();
                anchor.remove();
            } finally {
                URL.revokeObjectURL(downloadUrl);
            }
        } finally {
            URL.revokeObjectURL(objectUrl);
        }
    }
};
