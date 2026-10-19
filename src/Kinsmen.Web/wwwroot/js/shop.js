// Customer-side shop search and category filters. Product data stays server-rendered;
// this script only hides/shows cards, so stock and prices always come from the API.
document.addEventListener('DOMContentLoaded', function () {
    var grid = document.getElementById('shop-products');
    var search = document.getElementById('shop-search');
    var filters = document.getElementById('shop-category-filters');
    var resultCount = document.getElementById('shop-result-count');
    if (!grid || !search || !filters || !resultCount) return;

    var cards = Array.prototype.slice.call(grid.querySelectorAll('.shop-product'));
    var categories = cards.map(function (card) { return (card.dataset.productCategory || '').trim(); })
        .filter(function (category, index, all) { return category && all.indexOf(category) === index; })
        .sort(function (a, b) { return a.localeCompare(b); });
    var selectedCategory = '';

    function addFilter(label, category) {
        var button = document.createElement('button');
        button.type = 'button'; button.className = 'service-chip neo-pill-raised'; button.textContent = label;
        button.dataset.category = category;
        button.addEventListener('click', function () { selectedCategory = category; render(); });
        filters.appendChild(button);
    }
    addFilter('All products', '');
    categories.forEach(function (category) { addFilter(category, category); });

    function render() {
        var query = search.value.trim().toLocaleLowerCase();
        var visible = 0;
        cards.forEach(function (card) {
            var haystack = [card.dataset.productName, card.dataset.productCategory, card.dataset.productDescription].join(' ').toLocaleLowerCase();
            var matches = (!selectedCategory || card.dataset.productCategory === selectedCategory) && (!query || haystack.indexOf(query) !== -1);
            card.hidden = !matches;
            if (matches) visible++;
        });
        filters.querySelectorAll('button').forEach(function (button) {
            var active = button.dataset.category === selectedCategory;
            button.classList.toggle('active', active); button.classList.toggle('neo-pill-pressed', active); button.classList.toggle('neo-pill-raised', !active);
        });
        resultCount.textContent = visible === cards.length ? cards.length + ' products available' : visible + ' product' + (visible === 1 ? '' : 's') + ' found';
    }
    search.addEventListener('input', render);
    render();
});
