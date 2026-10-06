document.querySelector('.menu')?.addEventListener('click', event => {
  const open = document.querySelector('.navlinks').classList.toggle('open');
  event.currentTarget.setAttribute('aria-expanded', String(open));
});

document.querySelectorAll('[data-copy]').forEach(button => {
  button.addEventListener('click', async () => {
    try {
      await navigator.clipboard.writeText(button.getAttribute('data-copy'));
      const original = button.textContent;
      button.textContent = 'Copied';
      setTimeout(() => { button.textContent = original; }, 1700);
    } catch {
      button.textContent = 'Select text';
    }
  });
});

const searchInput = document.querySelector('#doc-search');
if (searchInput) {
  const results = document.querySelector('.search-results');
  let docs = [];
  function search() {
    const query = searchInput.value.trim().toLowerCase();
    results.replaceChildren();
    if (query.length < 2) return;
    const terms = query.split(/\s+/);
    const matches = docs.map(item => {
      const title = item.title.toLowerCase();
      const description = item.description.toLowerCase();
      const haystack = title + ' ' + description + ' ' + item.text.toLowerCase();
      if (!terms.every(term => haystack.includes(term))) return null;
      const score = (title.includes(query) ? 100 : 0)
        + terms.reduce((total, term) => total + (title.includes(term) ? 20 : 0)
          + (description.includes(term) ? 5 : 0), 0);
      return {item, score};
    }).filter(Boolean).sort((left, right) => right.score - left.score).slice(0, 12);
    for (const {item} of matches) {
      const link = document.createElement('a');
      link.href = item.url;
      const title = document.createElement('strong');
      title.textContent = item.title;
      const detail = document.createElement('small');
      detail.textContent = item.group + ' · ' + item.description;
      link.append(title, detail);
      results.append(link);
    }
    if (!matches.length) {
      const empty = document.createElement('small');
      empty.textContent = 'No matching pages';
      results.append(empty);
    }
  }
  fetch('search.json').then(response => response.json()).then(data => {
    docs = data;
    search();
  }).catch(() => {});
  searchInput.addEventListener('input', search);
  document.addEventListener('keydown', event => {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      searchInput.focus();
    }
    if (event.key === 'Escape' && document.activeElement === searchInput) {
      searchInput.value = '';
      results.replaceChildren();
      searchInput.blur();
    }
  });
}

const navToggle = document.querySelector('.doc-nav-toggle');
navToggle?.addEventListener('click', () => {
  const open = document.querySelector('.doc-nav').classList.toggle('open');
  navToggle.setAttribute('aria-expanded', String(open));
});
