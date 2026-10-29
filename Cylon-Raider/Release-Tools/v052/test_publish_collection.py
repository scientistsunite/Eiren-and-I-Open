import copy
import json
from pathlib import Path
import unittest
import publish_collection as release


class Guards(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.config=json.loads((Path(__file__).parent/'verified-build.json').read_text())
        release.configure(cls.config)
        cls.engine=release.engine

    def inventory(self):
        baseline=json.loads((Path(__file__).parent/'baseline.json').read_text())
        return {'id':self.engine.RELEASE_ID,'tag_name':self.engine.TAG,'immutable':False,'draft':False,'prerelease':True,'name':self.engine.TITLE,'body':baseline['body'],'assets':[{**asset,'state':'uploaded'} for asset in baseline['assets']]}

    def test_all_fifteen_existing_assets_are_preserved(self):
        self.assertEqual(len(self.engine.validate_release(self.inventory())),15)
        self.assertEqual(len(self.engine.BASELINE),15)

    def test_earlier_v05_asset_tampering_rejected(self):
        inventory=self.inventory()
        asset=next(asset for asset in inventory['assets'] if asset['name']=='CylonRaider-Setup-0.5.0-preliminary.exe')
        asset['digest']='sha256:'+'0'*64
        with self.assertRaises(ValueError): self.engine.validate_release(inventory)

    def test_concurrent_notes_change_rejected(self):
        inventory=self.inventory();inventory['body']+='unexpected edit'
        with self.assertRaises(ValueError): self.engine.validate_release(inventory)

    def test_only_v052_new_names_accepted(self):
        self.assertEqual(len(self.engine.NEW_FILES),6)
        self.assertTrue(all('0.5.2' in name for name in self.engine.NEW_FILES))
        self.assertTrue(set(self.engine.BASELINE).isdisjoint(self.engine.NEW_FILES))

    def test_only_scoped_owner_branch_allowed(self):
        env={'GITHUB_ACTIONS':'true','RUNNER_ENVIRONMENT':'github-hosted','GITHUB_REPOSITORY':self.engine.REPOSITORY,'GITHUB_ACTOR':'scientistsunite','GITHUB_EVENT_NAME':'push','GITHUB_REF':'refs/heads/cylon-raider-v052-release-addition'}
        self.engine.validate_actor(env)
        for key in env:
            changed=copy.copy(env);changed[key]='wrong'
            with self.assertRaises(ValueError): self.engine.validate_actor(changed)

    def test_notes_preserve_previous_version_sections(self):
        notes=self.engine.build_body()
        for version in ['v0.5.2 prerelease','v0.5.1 prerelease','v0.5.0 preliminary','v0.4.0']:
            self.assertIn(version,notes)
        self.assertIn(self.engine.SOURCE_SHA,notes)


if __name__=='__main__':unittest.main()
