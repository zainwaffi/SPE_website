const observers = new WeakMap();

export function initialize(element) {
    if (!element || observers.has(element)) {
        return;
    }

    const observer = new IntersectionObserver((entries) => {
        for (const entry of entries) {
            if (!entry.isIntersecting) {
                continue;
            }

            entry.target.classList.add("fade-up-visible");
            observer.unobserve(entry.target);
            observers.delete(entry.target);
        }
    }, {
        threshold: 0.12
    });

    element.classList.add("fade-up");
    observer.observe(element);
    observers.set(element, observer);
}

export function dispose(element) {
    const observer = observers.get(element);
    if (!observer) {
        return;
    }

    observer.disconnect();
    observers.delete(element);
}
