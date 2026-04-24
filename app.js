'use strict';


function hostInvoke(message) {
    if (!window.host) {
        return Promise.resolve(null);
    }

    return new Promise((resolve, reject) => {
        try {
            window.host(JSON.stringify(message), (result) => {
                resolve(result);
            });
        } catch (error) {
            reject(error);
        }
    });
}

window.translateHost = {
    ready: () => hostInvoke({ type: 'ready' }),
    toggle: () => hostInvoke({ type: 'toggle' }),
    show: () => hostInvoke({ type: 'show' }),
    hide: () => hostInvoke({ type: 'hide' }),
    close: () => hostInvoke({ type: 'close' }),
    isAutoStartEnabled: async () => (await hostInvoke({ type: 'isAutoStartEnabled' })) === 'true',
    setAutoStartEnabled: (enabled) => hostInvoke({ type: 'setAutoStartEnabled', enabled: !!enabled }),
    exit: () => hostInvoke({ type: 'exit' })
};

window.addEventListener('DOMContentLoaded', () => {
    window.translateHost.ready().catch(() => { });
}, { once: true });

window.addEventListener('beforeunload', (event) => {
    if (!window.translateHost) {
        return;
    }

    event.preventDefault();
    event.returnValue = '';
    window.translateHost.close().catch(() => { });
});

// 确保DOM加载完成后再执行
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initOptimization);
} else {
    initOptimization();
}

function initOptimization() {
    // 辅助函数：安全执行DOM操作
    function Element(el, func) {
        if (typeof el === "string") {
            el = document.querySelector(el);
        }
        try {
            if (el != null) func(el);
        } catch (error) {
            console.log(error);
        }
    }

    function RemoveElement(el) {
        Element(el, (el) => el.remove());
    }

    // 页面焦点事件优化
    window.onfocus = function () {
        let ipt = document.querySelector("#search_input");
        if (ipt) {
            window.scrollTo(0, 0);
            ipt.focus();
            ipt.select();
            // setTimeout(() => {
            // }, 100);
        }
    };

    window.onblur = function () {
        localStorage.removeItem("historyList");
    };

    // 延迟执行部分初始化操作
    setTimeout(function () {
        Element("#autosuggest-autosuggest__results", el => el.style.pointerEvents = "none");
        RemoveElement(".top-banner-wrap");
    }, 500);

    // 首页自动跳转
    if (window.location.href === 'https://dict.youdao.com/') {
        window.location.href = 'https://dict.youdao.com/result?word=.&lang=en';
        return;
    }

    // 移除不需要的元素
    const elementsToRemove = [
        ".feedbackBtn",
        ".small-logo",
        ".footer_container",
        ".top_nav-con",
        ".dict_indexes-con"
    ];
    elementsToRemove.forEach(selector => RemoveElement(selector));

    // 调整元素样式
    const styleModifications = [
        { selector: ".search_result-dict", style: "width", value: "100%" },
        { selector: ".lang_select-con", style: "width", value: "100%" },
        { selector: ".input_con-fixed", style: "padding", value: "0px" },
        { selector: ".search_result.center_container", style: "margin", value: "0px" },
        { selector: ".fixed-wrap.center_container", style: "margin", value: "auto" },
        { selector: "#searchLayout", style: "minWidth", value: "auto" }
    ];

    styleModifications.forEach(({ selector, style, value }) => {
        Element(selector, el => el.style[style] = value);
    });

    // 添加自定义CSS样式
    const customStyles = `
            .search_page .search_result[data-v-727a0d14] { margin-top:0px !important}
            .center_container { width: auto; min-width:auto; }
            .simple { margin-left:0px; }
            .search_page .input_con-fixed[data-v-727a0d14] { min-width:0px; position:relative !important}
            .simple .word-exp .trans[data-v-8042e1b4] { font-size:12px; line-height:inherit; }
            .search_page .word-head .phone_con { margin:0px; }
            .search_page .per-phone { margin-bottom:0px; }
            .search_page .word-head .title { margin-bottom:0px }
            .simple .exam_type[data-v-8042e1b4] { margin:0px; }
            .search_page .trans-container p { margin:0px; margin-bottom:0px; margin-top:0px; }
            .catalogue_author .dict-tabs[data-v-08c0bb43] { margin:0px; margin-bottom:0px; margin-top:0px; height:16px; }
            .per-phone { margin:0px; }
        `;

    const styleSheet = document.createElement("style");
    styleSheet.textContent = customStyles;
    document.head.appendChild(styleSheet);
}
