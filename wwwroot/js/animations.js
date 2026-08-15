window.fadeUpElement = (element) => {
    const observer = new IntersectionObserver(
        ([entry]) => {
            if (entry.isIntersecting) {

                // Wait one frame so the browser
                // registers the initial state first
                requestAnimationFrame(() => {
                    element.classList.remove("opacity-0", "translate-y-8");
                    element.classList.add("opacity-100", "translate-y-0");
                });

                observer.unobserve(element);
            }
        },
        {
            threshold: 0.2
        }
    );

    observer.observe(element);
};