---
pubdate: 2021-05-22T00:23:00Z
category: general
---

# Tweet changes and EDDN changes

Hey everyone! We're doing some changes to both Tweets and our EDDN integration.

## Tweet changes

We're doing some overhauls to the currently spammy tweeting with barely no changes.

From now on:

- We will only send the tweets every monday at 00:30 UTC
- The stats will start to contain diffs from the latest tweet

So, instead of looking like this

```
Nightly stats #EliteDangerous

833 users registered
44.7 thousand journals saved
69.7 million lines of journal
https://journal-limpet.com
```

.. we will start using this format

```
Weekly stats #EliteDangerous

1 user(s) registered (Total 833)
5 journal(s) saved (Total 44.7 thousand)
4 thousand lines(s) saved (Total 69.7 million)
https://journal-limpet.com
```

---

## EDDN changes

I already announced this on [Twitter](https://twitter.com/JournalLimpet/status/1535581640529006592),
but we've improved the integration with EDDN

### Improved EDDN integration
Earlier, we only sent data for the Journal schema in EDDN.
But now with release `2022.06.10.1645`, we support a bit more

- ApproachSettlement
- CodexEntry
- FSSAllBodiesFound
- FSSBodySignals
- FSSDiscoveryScan
- NavBeaconScan
- ScanBaryCentre

The old Journal consists of these events in the journal:
- Docked
- FSDJump
- Scan
- Location
- SAASignalsFound
- CarrierJump

So, what this means, is that now we send even more data through EDDN
(as long as it doesn't require us to have access to other game files)