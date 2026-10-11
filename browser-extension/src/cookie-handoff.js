void MTDI18n.ready.then(() => {
  MTDI18n.apply(document);
  const target = new URL(location.href).searchParams.get('target');
  try {
    const uri = new URL(target);
    const reconstructed = MTDUrls.protocolUrl(uri.searchParams.get('url'), {
      mode:uri.searchParams.get('action')==='quick-queue'?'queue':uri.searchParams.get('action')==='open'?'open':'analyze',
      quality:uri.searchParams.get('quality'),container:uri.searchParams.get('container')});
    if(uri.protocol === 'moderntubedownloader:' && reconstructed) document.getElementById('continue').href = reconstructed;
  } catch { /* A malformed extension URL never becomes an external navigation. */ }
});
